using System;
using Newtonsoft.Json.Linq;

namespace XRSuperspace
{
    public static class DatasetLabels
    {
        public const string Canonical="CANONICAL MODEL";
        public const string RealMaterial="REAL-MATERIAL DATASET";
        public const string Unvalidated="UNVALIDATED IMPORT";
        public static void RequireCanonical(JObject envelope)
        {
            var m=envelope?["metadata"];
            // Historical canonical fixtures have no optional label. All kinds
            // and units remain mandatory; labels cannot override discriminants.
            if((string)m?["kind"]!="authoritative_canonical_modelset" ||
               (string)m?["material"]?["kind"]!="canonical_model" ||
               (string)m?["material"]?["units"]!="dimensionless" ||
               (string)m?["coordinate_units"]!="dimensionless" ||
               (envelope?["label"]!=null && (string)envelope["label"]!=Canonical))
                throw new FormatException(Unvalidated+": this loader only accepts "+Canonical+"; use the separate material adapter/validation report");
        }
    }
}
