using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace XRSuperspace
{
    // Read-only inspection/export: consumes existing model and CURRENT finite
    // sums. Never computes from display transforms or labels stale values Ready.
    public static class DesktopResearchTools
    {
        static string Number(double value)=>value.ToString("G17",CultureInfo.InvariantCulture);
        public static string Overview(ScientificDemo demo)
        {
            var state=demo.Lab.State;if(state==null) return "Loading model...";var model=state.Model;
            return $"{model.SelectedCount} selected / {state.Dataset.Candidates.Count} candidates\n{state.Edges.Length} lattice-neighbour edges\n{model.BoundaryCount} boundary candidates\n{model.Journal.Count} recorded states\n\nOffset x   {Number(model.Phi.X)}\nOffset y   {Number(model.Phi.Y)}\n\n{demo.Comparison.Summary}\n\nCoordinates are dimensionless model units. Lines are not chemical bonds.";
        }
        public static string Point(ScientificDemo demo)
        {
            var state=demo.Lab.State;int row=state?.SelectedRow??-1;
            if(row<0) return "Click a point in Structure or Selection window to inspect its exact identity. The white marker links the same point in both views.";
            var id=state.Selection.Value;var data=state.Dataset;double x=data.Project(id,0),y=data.Project(id,1),z=data.Project(id,2),u=data.Project(id,3),v=data.Project(id,4);
            double distance=Membership.Distance(data.DoubleWindow,new Double2(u+state.Phi.X,v+state.Phi.Y));
            return $"Exact lattice identity\nn = ({id})\n\nPhysical coordinates\nx   {Number(x)}\ny   {Number(y)}\nz   {Number(z)}\n\nSelection coordinates + offset\nu   {Number(u+state.Phi.X)}\nv   {Number(v+state.Phi.Y)}\n\nWindow classification   {state.States[row]}\nActive in current subset   {(state.Model.Active[row]?"Yes":"No")}\nSigned window distance\n{Number(distance)}\n\nDimensionless model units. Boundary policy and subset filters remain those of the dataset.";
        }
        public static string Diffraction(ScientificDemo demo)
        {
            var reciprocal=demo.Instrument.Reciprocal;
            if(!reciprocal.Current) return "Updating\n\nA calculation is in flight. Numerical intensities are withheld until this revision is Ready.";
            int row=reciprocal.SelectedPeak;
            if(row<0||row>=reciprocal.Peaks.Length) return $"Ready / {reciprocal.Peaks.Length} sampled groups\n\nClick a diffraction peak to read q, complex amplitude and intensity.\n\nUnit scattering weights\nA(q) = sum exp(+i q.r)\nI(q) = |A(q)| squared\nNormalized intensity = I / N squared\n\nCalculated finite-patch diffraction, not a measured image. q uses reciprocal model units.";
            var peak=reciprocal.Peaks[row];var value=reciprocal.Values[row];
            return $"Ready / sample {row}\nh = ({string.Join("; ",peak.H.Select(id=>id.ToString()))})\n\nReciprocal vector\nqx   {Number(peak.Q.X)}\nqy   {Number(peak.Q.Y)}\nqz   {Number(peak.Q.Z)}\n\nComplex amplitude\nReal   {Number(value[0])}\nImag   {Number(value[1])}\n\nIntensity   {Number(value[2])}\nI / N squared   {Number(value[3])}\n\nN = {demo.Lab.State.SelectedCount}\nUnit weights; +i q.r phase.\nCalculated, not measured.";
        }
        public static string Export(DesktopWorkspace workspace,string root)
        {
            return Export(workspace.Demo,root,workspace.CanSave);
        }
        public static string Export(ScientificDemo demo,string root,bool canSave)
        {
            var reciprocal=demo.Instrument.Reciprocal;
            if(!canSave||!reciprocal.Current) throw new InvalidOperationException("Wait for diffraction Ready before exporting numerical results.");
            var state=demo.Lab.State;int revision=state.Model.Revision;string key=state.Model.Key(reciprocal.Config);
            string folder=Path.Combine(Path.GetFullPath(root),"results-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            var points=new StringBuilder("n0,n1,n2,n3,n4,x,y,z,u,v,u_plus_offset,v_plus_offset,window_classification\n");
            for(int i=0;i<state.Dataset.Candidates.Count;i++)
            {
                if(!state.Model.Active[i]) continue;var id=state.Dataset.Candidates[i].Id;
                points.Append(id).Append(',');for(int j=0;j<5;j++) points.Append(Number(state.Dataset.Project(id,j))).Append(',');
                points.Append(Number(state.Dataset.Project(id,3)+state.Phi.X)).Append(',').Append(Number(state.Dataset.Project(id,4)+state.Phi.Y)).Append(',').Append(state.States[i]).Append('\n');
            }
            var peaks=new StringBuilder("sample,qx,qy,qz,amplitude_real,amplitude_imag,intensity,intensity_over_N_squared\n");
            for(int i=0;i<reciprocal.Peaks.Length;i++)
            {
                var q=reciprocal.Peaks[i].Q;var values=reciprocal.Values[i];peaks.Append(i).Append(',').Append(Number(q.X)).Append(',').Append(Number(q.Y)).Append(',').Append(Number(q.Z));
                foreach(double value in values) peaks.Append(',').Append(Number(value));peaks.Append('\n');
            }
            if(revision!=state.Model.Revision||key!=state.Model.Key(reciprocal.Config)||!reciprocal.Current) throw new InvalidOperationException("State changed during export; result refused.");
            var hashes=new JObject();
            void Write(string name,string content)
            {
                byte[] bytes=new UTF8Encoding(false).GetBytes(content);using(var file=new FileStream(Path.Combine(folder,name),FileMode.CreateNew)) file.Write(bytes,0,bytes.Length);
                hashes[name]=Qc5dParser.Sha256(bytes);
            }
            Write("selected_points.csv",points.ToString());Write("finite_diffraction.csv",peaks.ToString());
            Write("README.txt","AperiTrace numerical export. Dimensionless canonical model, not a refined material. Physical and selection coordinates are exact double-precision model projections, not display positions. CSV rows use invariant decimal notation. Selected points respect window boundary policy and current subset filters. Diffraction includes all 381 sampled q groups, real unit weights, +i q.r phase, I/N^2 normalization (zero when empty). See state.json for the checksum-linked model, journal, filters and reciprocal contributors. No claim of experimental diffraction.\n");
            Write("state.json",new JObject { ["schema"]="aperitrace-numerical-export/1",["exported_utc"]=DateTimeOffset.UtcNow.ToString("O"),["result_key"]=key,["files_sha256"]=hashes,
                ["state"]=demo.Capture.State(UnityEngine.Screen.width,UnityEngine.Screen.height) }.ToString());
            return folder;
        }
    }
}
