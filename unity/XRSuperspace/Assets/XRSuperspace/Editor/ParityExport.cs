using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRSuperspace.Editor
{
    // Reads only input phi/q and runtime inputs. Never reads Python expected.json.
    public static class ParityExport
    {
        static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
        static readonly JsonSerializerSettings JsonSettings=new JsonSerializerSettings { Formatting=Formatting.Indented,FloatFormatHandling=FloatFormatHandling.String };
        static JObject Config()
        {
            var path=Path.Combine(Root,"configs/parity_v1.json");
            if(new FileInfo(path).Length>65536) throw new FormatException("parity config exceeds budget");
            var c=JObject.Parse(File.ReadAllText(path));
            if((string)c["schema_version"]!="1.0.0"||(string)c["fixture_version"]!="canonical_parity_v1"||
                (string)c["source_fixture"]!="tests/fixtures/canonical_small_v1") throw new FormatException("unsupported parity input");
            var cases=(JArray)c["cases"];var qs=(JArray)c["q_vectors"];
            if(cases.Count<4||cases.Count>64||qs.Count<1||qs.Count>128) throw new FormatException("parity workload exceeds budget");
            var names=new HashSet<string>();
            foreach(var item in cases) {if(!names.Add((string)item["name"])) throw new FormatException("duplicate case name");Vector((JArray)item["phi"],2,.25);}
            foreach(var q in qs) Vector((JArray)q,3,1e4);
            return c;
        }
        static double[] Vector(JArray a,int count,double limit)
        {
            if(a==null||a.Count!=count) throw new FormatException("wrong vector dimension");
            var values=a.Select(x=>(double)x).ToArray();
            if(values.Any(x=>double.IsNaN(x)||double.IsInfinity(x)||Math.Abs(x)>limit)) throw new FormatException("nonfinite/out-of-domain parity vector");
            return values;
        }
        static RuntimeDataset Parsed()=>Qc5dParser.Load(Path.Combine(Root,"tests/fixtures/canonical_small_v1/canonical.qc5d"));
        static double[][] Basis(RuntimeDataset d)=>Enumerable.Range(0,5).Select(i=>Enumerable.Range(0,5).Select(j=>d.Basis(i,j)).ToArray()).ToArray();
        static double[][] Window(IReadOnlyList<Double2> w)=>w.Select(x=>new[]{x.X,x.Y}).ToArray();
        static object[] Edges(RuntimeDataset d,IReadOnlyList<GraphEdge> edges)=>edges.Select(e=>(object)new object[]{d.Candidates[e.Source].Id.ToString(),d.Candidates[e.Target].Id.ToString(),e.Generator,(int)e.Sign}).ToArray();
        static Dictionary<string,string> SourceHashes()
        {
            string folder=Path.Combine(Root,"unity/XRSuperspace/Assets/XRSuperspace");
            return Directory.GetFiles(folder,"*",SearchOption.AllDirectories)
                .Where(p=>p.EndsWith(".cs",StringComparison.Ordinal)||p.EndsWith(".compute",StringComparison.Ordinal)||p.EndsWith(".shader",StringComparison.Ordinal))
                .OrderBy(p=>p,StringComparer.Ordinal).ToDictionary(p=>p.Substring(Root.Length+1).Replace('\\','/'),p=>Qc5dParser.Sha256(File.ReadAllBytes(p)));
        }
        static Dictionary<string,object> Base(RuntimeDataset d)=>new Dictionary<string,object> {
            ["schema_version"]="1.0.0",["config_sha256"]=Qc5dParser.Sha256(File.ReadAllBytes(Path.Combine(Root,"configs/parity_v1.json"))),
            ["binary_sha256"]=d.Metadata.BinarySha256,["candidate_ids"]=d.Candidates.Select(x=>x.Id.ToString()).ToArray(),
            ["code_sha256"]=SourceHashes(),["executed_at_utc"]=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture),
            ["assembly_sha256"]=new[]{typeof(CanonicalMath).Assembly,typeof(GpuPointBuffers).Assembly,typeof(ParityExport).Assembly}
                .ToDictionary(a=>a.GetName().Name,a=>Qc5dParser.Sha256(File.ReadAllBytes(a.Location)))
        };
        static object Case(RuntimeDataset d,JToken input,JArray queries)
        {
            var p=Vector((JArray)input["phi"],2,.25);var phi=new Double2(p[0],p[1]);
            var model=new InteractionModel(d);model.Apply(phi,model.Filter);
            var selected=Enumerable.Range(0,d.Candidates.Count).Where(i=>model.Active[i]).ToArray();
            var samples=queries.Select(q=> {var a=Vector((JArray)q,3,1e4);return ReciprocalCalculation.Sample(model,new Double3(a[0],a[1],a[2]));}).ToArray();
            return new Dictionary<string,object> {
                ["name"]=(string)input["name"],["phi"]=p,["states"]=model.States.Select(s=>(int)s).ToArray(),
                ["signed_distance"]=d.Candidates.Select(c=>Membership.Distance(d.DoubleWindow,new Double2(d.Project(c.Id,3)+phi.X,d.Project(c.Id,4)+phi.Y))).ToArray(),
                ["selected_ids"]=selected.Select(i=>d.Candidates[i].Id.ToString()).ToArray(),
                ["boundary_ids"]=Enumerable.Range(0,d.Candidates.Count).Where(i=>model.States[i]==PointState.Boundary).Select(i=>d.Candidates[i].Id.ToString()).ToArray(),
                ["selected_count"]=selected.Length,
                ["physical_selected"]=selected.Select(i=>new[]{d.Project(d.Candidates[i].Id,0),d.Project(d.Candidates[i].Id,1),d.Project(d.Candidates[i].Id,2)}).ToArray(),
                ["internal_selected"]=selected.Select(i=>new[]{d.Project(d.Candidates[i].Id,3)+phi.X,d.Project(d.Candidates[i].Id,4)+phi.Y}).ToArray(),
                ["edges"]=Edges(d,model.Edges),["amplitudes"]=samples.Select(s=>new[]{s[0],s[1]}).ToArray(),
                ["intensity_raw"]=samples.Select(s=>s[2]).ToArray(),["intensity_normalized"]=samples.Select(s=>s[3]).ToArray()
            };
        }
        static object ParserDump(RuntimeDataset d)
        {
            var m=d.Metadata;
            return new Dictionary<string,object> {
                ["header"]=new Dictionary<string,object> {
                    ["schema_version"]="1.0.0",["schema_major"]=1,["schema_minor"]=0,["flags"]=m.IncludeBoundary?7:3,
                    ["candidate_count"]=d.Candidates.Count,["window_vertex_count"]=d.Window.Count,["edge_count"]=d.Edges.Count,
                    ["authoritative_sha256"]=m.AuthoritativeSha256,["payload_sha256"]=m.PayloadSha256,
                    ["display_scales"]=new[]{m.DisplayScales.X,m.DisplayScales.Y,m.DisplayScales.Z},["phi0"]=new[]{m.Phi0.X,m.Phi0.Y},
                    ["boundary_policy"]=m.IncludeBoundary?"include":"exclude",["authoritative_boundary_epsilon"]=m.AuthorityEpsilon,
                    ["preview_boundary_epsilon"]=m.PreviewEpsilon,["coordinate_atol"]=m.CoordinateTolerance,["boundary_distance_atol"]=m.DistanceTolerance,
                    ["binary_size_bytes"]=504L+54L*d.Candidates.Count+12L*d.Edges.Count,
                    ["binary_sha256"]=m.BinarySha256,["authoritative_source_file_verified"]=m.AuthoritativeSourceFileVerified
                },
                ["basis"]=Basis(d),["window"]=Window(d.Window),
                ["lattice_n"]=d.Candidates.Select(c=>Enumerable.Range(0,5).Select(j=>c.Id[j]).ToArray()).ToArray(),
                ["candidate_ids"]=d.Candidates.Select(c=>c.Id.ToString()).ToArray(),
                ["hashes"]=d.Candidates.Select(c=>c.StableHash.ToString(CultureInfo.InvariantCulture)).ToArray(),
                ["physical"]=d.Candidates.Select(c=>new[]{c.Physical.X,c.Physical.Y,c.Physical.Z}).ToArray(),
                ["internal"]=d.Candidates.Select(c=>new[]{c.Internal.X,c.Internal.Y}).ToArray(),
                ["distance"]=d.Candidates.Select(c=>(double)c.Distance).ToArray(),["selected"]=d.Selected.ToArray(),["boundary"]=d.Boundary.ToArray(),
                ["edges"]=d.Edges.Select(e=>new[]{e.Source,e.Target,e.Generator,(int)e.Sign,0}).ToArray()
            };
        }
        static void Write(string name,object value)
        {
            string directory=Path.Combine(Root,"results/parity");Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,name),JsonConvert.SerializeObject(value,JsonSettings)+"\n");
        }
        public static Dictionary<string,object> CpuSnapshot(RuntimeDataset parsed,JObject c)
        {
            if(parsed==null||c==null) throw new ArgumentNullException();
            var d=CanonicalMath.Reconstruct(parsed);var output=Base(parsed);
            output["computation"]="independent C5 equations / C# double / positive u+phi / exp(+iq.r)";
            output["basis"]=Basis(d);output["window"]=Window(d.DoubleWindow);
            output["lattice_n"]=d.Candidates.Select(x=>Enumerable.Range(0,5).Select(j=>x.Id[j]).ToArray()).ToArray();
            output["hashes"]=d.Candidates.Select(x=>x.StableHash.ToString(CultureInfo.InvariantCulture)).ToArray();
            output["physical"]=d.Candidates.Select(x=>new[]{x.Physical.X,x.Physical.Y,x.Physical.Z}).ToArray();
            output["internal"]=d.Candidates.Select(x=>new[]{x.Internal.X,x.Internal.Y}).ToArray();
            output["cases"]=((JArray)c["cases"]).Select(i=>Case(d,i,(JArray)c["q_vectors"])).ToArray();
            output["parser"]=ParserDump(parsed);return output;
        }
        public static void BatchCpu()
        {
            try {
                Write("csharp_computed.json",CpuSnapshot(Parsed(),Config()));
                Debug.Log("[XRSuperspace] Independent CPU parity computation exported.");EditorApplication.Exit(0);
            } catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static Dictionary<string,object> GpuSnapshot(RuntimeDataset d,JObject c)
        {
            Mesh mesh=null;
            try {
                if(!SystemInfo.supportsComputeShaders||!SystemInfo.supportsInstancing||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)
                    throw new NotSupportedException("actual compute GPU unavailable; CPU simulation cannot certify parity");
                if(d==null||c==null) throw new ArgumentNullException();
                var output=Base(d);
                output["environment"]="Unity Editor actual compute dispatch/readback (not Quest)";
                output["unity_version"]=Application.unityVersion;output["graphics_api"]=SystemInfo.graphicsDeviceType.ToString();
                output["device"]=SystemInfo.graphicsDeviceName;output["driver"]=SystemInfo.graphicsDeviceVersion;
                output["preview_epsilon"]=d.Metadata.PreviewEpsilon;
                var candidates=d.Candidates.Select(x=>new PreviewCandidate {Physical=new Vector4((float)x.Physical.X,(float)x.Physical.Y,(float)x.Physical.Z,0),Uv=new Vector2((float)x.Internal.X,(float)x.Internal.Y),Allowed=1}).ToArray();
                mesh=new Mesh {name="ParityArgsOnly"};mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.triangles=new[]{0,1,2};
                var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/XRSuperspace/Shaders/PreviewMembership.compute");
                var cases=new List<object>();
                using(var gpu=new GpuPointBuffers(compute,candidates,d.DoubleWindow.ToArray(),mesh))
                    foreach(var input in (JArray)c["cases"]) {
                        var p=Vector((JArray)input["phi"],2,.25);gpu.Dispatch(new Double2(p[0],p[1]),d.Metadata.PreviewEpsilon,d.Metadata.IncludeBoundary,1,false,true);
                        var states=new uint[d.Candidates.Count];var active=new uint[d.Candidates.Count];gpu.Read(states,active);
                        cases.Add(new {name=(string)input["name"],phi=p,states,active,selected_count=active.Count(x=>x==1),boundary_count=states.Count(x=>x==2)});
                    }
                output["cases"]=cases;return output;
            }
            finally {if(mesh!=null) UnityEngine.Object.DestroyImmediate(mesh);}
        }
        public static void BatchGpu()
        {
            try {Write("gpu_computed.json",GpuSnapshot(Parsed(),Config()));Debug.Log("[XRSuperspace] Actual GPU parity dispatch/readback exported.");EditorApplication.Exit(0);}
            catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
