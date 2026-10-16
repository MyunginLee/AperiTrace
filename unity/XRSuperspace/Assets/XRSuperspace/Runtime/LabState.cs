using System;

namespace XRSuperspace
{
    // Model/state has no dependency on Unity transforms, cameras or input devices.
    public sealed class LabState
    {
        public RuntimeDataset Dataset { get; }
        public InteractionModel Model {get;}
        public Double2 Phi => Model.Phi;
        public PointState[] States => Model.States;
        public GraphEdge[] Edges => Model.Edges;
        public LatticeId? Selection { get; private set; }
        public int SelectedCount => Model.SelectedCount;
        public int BoundaryCount => Model.BoundaryCount;
        public LabState(RuntimeDataset data) { Dataset=data;Model=new InteractionModel(data); }
        public void SetPhi(Double2 phi)
        {
            Model.Apply(phi,Model.Filter);
        }
        public void Select(int row) { if(row<0||row>=Dataset.Candidates.Count) throw new ArgumentOutOfRangeException(nameof(row));Selection=Dataset.Candidates[row].Id; }
        public void ClearSelection()=>Selection=null;
        public int SelectedRow { get { if(!Selection.HasValue) return -1;for(int i=0;i<Dataset.Candidates.Count;i++) if(Dataset.Candidates[i].Id.Equals(Selection.Value)) return i;return -1; } }
    }
}
