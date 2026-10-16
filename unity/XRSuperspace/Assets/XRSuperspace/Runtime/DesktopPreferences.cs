using System;
using UnityEngine;
using Newtonsoft.Json;

namespace XRSuperspace
{
    [Serializable]
    public sealed class DesktopPreferences
    {
        public int TextSize;
        public bool HighContrast,ReducedMotion,AlternativePalette,SkipWelcome;
        public DiffractionDisplay Diffraction=new DiffractionDisplay();
        const string Key="AperiTrace.Desktop.v1";
        public float TextScale=>new[]{1f,1.15f,1.3f}[Mathf.Clamp(TextSize,0,2)];
        public static DesktopPreferences Load()
        {
            DesktopPreferences value;
            try {value=JsonConvert.DeserializeObject<DesktopPreferences>(PlayerPrefs.GetString(Key,"{}"))??new DesktopPreferences();}
            catch {value=new DesktopPreferences();}
            value.TextSize=Mathf.Clamp(value.TextSize,0,2);
            try {value.Diffraction??=new DiffractionDisplay();value.Diffraction.Validate();}catch {value.Diffraction=new DiffractionDisplay();}
            return value;
        }
        public void Save() {Diffraction.Validate();TextSize=Mathf.Clamp(TextSize,0,2);PlayerPrefs.SetString(Key,JsonConvert.SerializeObject(this));PlayerPrefs.Save();}
    }
}
