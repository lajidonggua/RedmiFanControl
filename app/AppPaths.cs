using System;
using System.IO;
namespace RedmiFanControl {
    // Installation location is independent of writable preferences and telemetry.
    static class AppPaths {
        public static readonly string DataRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RedmiFanControl");
        public static string Runs { get {return Path.Combine(DataRoot,"runs");} }
        public static string Configuration { get {return Path.Combine(DataRoot,"config","fan-control.json");} }
        public static string Modules { get {return Path.Combine(DataRoot,"runtime","pawnio-modules-0.2.11");} }
        public static string DriverLibrary { get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"PawnIO","PawnIOLib.dll");} }
        public static string LegacyConfiguration {
            get {
                string beside=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"config","fan-control.json");
                if(File.Exists(beside))return beside;
                var directory=new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                if(directory.Name.StartsWith("bin",StringComparison.OrdinalIgnoreCase) && directory.Parent!=null && directory.Parent.Name=="app" && directory.Parent.Parent!=null)
                    return Path.Combine(directory.Parent.Parent.FullName,"config","fan-control.json");
                return null;
            }
        }
    }
}
