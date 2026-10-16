using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XRSuperspace
{
    // Scientific subset of the exported candidate patch; never a display-space test.
    public sealed class AnalysisFilter
    {
        public bool CropEnabled,SliceEnabled;
        public string CropKind="cylinder";
        public double Radius=4,ZMax=1.5,SliceZ,SliceHalfWidth=.25;
        public AnalysisFilter Copy()=>(AnalysisFilter)MemberwiseClone();
        public void Validate()
        {
            foreach(double x in new[]{Radius,ZMax,SliceZ,SliceHalfWidth}) if(double.IsNaN(x)||double.IsInfinity(x)||Math.Abs(x)>1e6) throw new ArgumentException("filter must be finite and bounded");
            if(Radius<=0||ZMax<=0||SliceHalfWidth<=0||(CropKind!="cylinder"&&CropKind!="sphere")) throw new ArgumentException("invalid crop/slice");
        }
        public bool Contains(RuntimeDataset data,LatticeId id)
        {
            double x=data.Project(id,0),y=data.Project(id,1),z=data.Project(id,2);
            if(CropEnabled&&(CropKind=="sphere"?x*x+y*y+z*z>Radius*Radius:x*x+y*y>Radius*Radius||Math.Abs(z)>ZMax)) return false;
            return !SliceEnabled||Math.Abs(z-SliceZ)<=SliceHalfWidth;
        }
        public string Key=>string.Join("/",CropEnabled,SliceEnabled,CropKind,Radius.ToString("R",CultureInfo.InvariantCulture),ZMax.ToString("R",CultureInfo.InvariantCulture),SliceZ.ToString("R",CultureInfo.InvariantCulture),SliceHalfWidth.ToString("R",CultureInfo.InvariantCulture));
    }
    public sealed class ChangeFrame
    {
        public int Frame;
        public string DatasetChecksum;
        public double[] Phi;
        public AnalysisFilter Filter;
        public string[] Selected,Boundary,Entered,Exited;
    }
    public sealed class InteractionModel
    {
        public RuntimeDataset Dataset {get;}
        public Double2 Phi {get;private set;}
        public AnalysisFilter Filter {get;private set;}=new AnalysisFilter();
        public PointState[] States {get;private set;}
        public bool[] Active {get;private set;}
        public GraphEdge[] Edges {get;private set;}
        public int SelectedCount {get;private set;}
        public int BoundaryCount {get;private set;}
        public int Revision {get;private set;}
        public double LastMembershipMilliseconds {get;private set;}
        IReadOnlyList<ChangeFrame> journalView;
        public IReadOnlyList<ChangeFrame> Journal=>journalView??=journal.AsReadOnly();
        public const int MaximumFrames=4096,MaximumRecordedIds=2000000;
        readonly List<ChangeFrame> journal=new();
        readonly string[] ids;
        int recordedIds;
        public InteractionModel(RuntimeDataset data)
        {
            Dataset=data;ids=new string[data.Candidates.Count];for(int i=0;i<ids.Length;i++) ids[i]=data.Candidates[i].Id.ToString();
            Apply(data.Metadata.Phi0,Filter);
        }
        public bool Apply(Double2 phi,AnalysisFilter filter)
        {
            if(double.IsNaN(phi.X)||double.IsNaN(phi.Y)||double.IsInfinity(phi.X)||double.IsInfinity(phi.Y)) throw new ArgumentException("phi must be finite");
            if(filter==null) throw new ArgumentNullException(nameof(filter));filter.Validate();
            if(States!=null&&phi.X==Phi.X&&phi.Y==Phi.Y&&filter.Key==Filter.Key) return false;
            var membershipTimer=System.Diagnostics.Stopwatch.StartNew();var states=Membership.Evaluate(Dataset,phi);membershipTimer.Stop();double membershipMilliseconds=membershipTimer.Elapsed.TotalMilliseconds;var active=new bool[states.Length];var graphStates=(PointState[])states.Clone();
            var selected=new List<string>();var boundary=new List<string>();var entered=new List<string>();var exited=new List<string>();
            for(int i=0;i<states.Length;i++)
            {
                active[i]=Membership.IsSelected(states[i],Dataset.Metadata.IncludeBoundary)&&filter.Contains(Dataset,Dataset.Candidates[i].Id);
                if(!active[i]) graphStates[i]=PointState.Rejected;
                if(active[i]) selected.Add(ids[i]);if(states[i]==PointState.Boundary) boundary.Add(ids[i]);
                if(Active!=null) {if(active[i]&&!Active[i]) entered.Add(ids[i]);if(!active[i]&&Active[i]) exited.Add(ids[i]);}
            }
            int size=selected.Count+boundary.Count+entered.Count+exited.Count;
            if(journal.Count>=MaximumFrames||recordedIds+size>MaximumRecordedIds) throw new InvalidOperationException("event journal budget reached: save and explicitly clear journal before changing model");
            var frame=new ChangeFrame {Frame=journal.Count,DatasetChecksum=Dataset.Metadata.BinarySha256,Phi=new[]{phi.X,phi.Y},Filter=filter.Copy(),Selected=selected.ToArray(),Boundary=boundary.ToArray(),Entered=entered.ToArray(),Exited=exited.ToArray()};
            // Commit/log synchronously. Presentation time and fade progress are absent.
            Phi=phi;Filter=filter.Copy();States=states;Active=active;Edges=Membership.Graph(Dataset,graphStates);SelectedCount=selected.Count;BoundaryCount=boundary.Count;
            journal.Add(frame);recordedIds+=size;Revision++;LastMembershipMilliseconds=membershipMilliseconds;return true;
        }
        public void ClearJournal()
        {
            journal.Clear();recordedIds=0;
            var selected=new List<string>();var boundary=new List<string>();for(int i=0;i<ids.Length;i++) {if(Active[i]) selected.Add(ids[i]);if(States[i]==PointState.Boundary) boundary.Add(ids[i]);}
            journal.Add(new ChangeFrame {Frame=0,DatasetChecksum=Dataset.Metadata.BinarySha256,Phi=new[]{Phi.X,Phi.Y},Filter=Filter.Copy(),Selected=selected.ToArray(),Boundary=boundary.ToArray(),Entered=Array.Empty<string>(),Exited=Array.Empty<string>()});recordedIds=selected.Count+boundary.Count;
        }
        // Restore only a fully recomputed, ordered journal ending at this exact model.
        // No coordinates, masks or revisions are replaced by serialized claims.
        public void RestoreJournal(ChangeFrame[] frames)
        {
            if(frames==null||frames.Length==0||frames.Length>MaximumFrames) throw new FormatException("journal frame budget invalid");
            var replay=new InteractionModel(Dataset);
            for(int i=0;i<frames.Length;i++)
            {
                var f=frames[i];if(f==null||f.Frame!=i||f.DatasetChecksum!=Dataset.Metadata.BinarySha256||f.Phi==null||f.Phi.Length!=2) throw new FormatException("journal identity/order invalid");
                replay.Apply(new Double2(f.Phi[0],f.Phi[1]),f.Filter);if(i==0) replay.ClearJournal();
                if(replay.Journal.Count!=i+1) throw new FormatException("journal contains duplicate/no-op frame");var a=replay.Journal[i];
                bool Same(string[] x,string[] y) {if(x==null||x.Length!=y.Length) return false;for(int j=0;j<x.Length;j++) if(x[j]!=y[j]) return false;return true;}
                if(!Same(f.Selected,a.Selected)||!Same(f.Boundary,a.Boundary)||!Same(f.Entered,a.Entered)||!Same(f.Exited,a.Exited)) throw new FormatException("journal exact IDs do not match CPU replay");
            }
            if(replay.Phi.X!=Phi.X||replay.Phi.Y!=Phi.Y||replay.Filter.Key!=Filter.Key) throw new FormatException("journal endpoint does not match session model");
            journal.Clear();journal.AddRange(replay.journal);recordedIds=replay.recordedIds;
        }
        public string Key(string config)
        {
            string value=Dataset.Metadata.BinarySha256+"|"+Phi.X.ToString("R",CultureInfo.InvariantCulture)+"|"+Phi.Y.ToString("R",CultureInfo.InvariantCulture)+"|"+Filter.Key+"|"+config;
            using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();
        }
        public Double3 Position(int row) {var id=Dataset.Candidates[row].Id;return new Double3(Dataset.Project(id,0),Dataset.Project(id,1),Dataset.Project(id,2));}
    }
    public static class ReciprocalCalculation
    {
        // Positive exp(+i q.r), real unit weights, no taper, I / N_selected^2.
        public static double[] Sample(InteractionModel model,Double3 q)
        {
            double re=0,im=0;for(int i=0;i<model.Active.Length;i++) if(model.Active[i]) {var r=model.Position(i);double phase=q.X*r.X+q.Y*r.Y+q.Z*r.Z;re+=Math.Cos(phase);im+=Math.Sin(phase);}
            double raw=re*re+im*im;return new[]{re,im,raw,model.SelectedCount==0?0:raw/((double)model.SelectedCount*model.SelectedCount)};
        }
    }
}
