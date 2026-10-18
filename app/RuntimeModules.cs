using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
namespace RedmiFanControl {
    // Official signed modules and their exact corresponding source travel inside the EXE.
    // Extraction is performed by the unelevated UI; elevated workers only verify/read.
    static class RuntimeModules {
        static readonly string[] Names={"LpcIO.bin","AMDFamily17.bin","COPYING","source.zip"};
        static readonly string[] Hashes={
            "B3896A1CAB0D808FCA31FE2EBCAE045D59DAC690DA87B17C858BB8DA357EB45E",
            "DAE74615761B78BDF064DFB3E136252DDCC6FC727D88F14738D0E5800D427A91",
            "1E7E6BAE5A5BDE32F1AE5A7C37A082D1AB03CF89354F7F936AC40BE9E39A6531",
            "3766588ABEC0A353AA8824A1F138A443A4E54C62A38EEC30B2D44D770C59E62A"};
        static string Hash(Stream data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","");}
        static bool Matches(string path,string expected){if(!File.Exists(path))return false;using(var input=File.OpenRead(path))return Hash(input)==expected;}
        public static void Prepare(string directory){
            Directory.CreateDirectory(directory);
            for(int i=0;i<Names.Length;i++){
                string destination=Path.Combine(directory,Names[i]);
                if(Matches(destination,Hashes[i]))continue;
                using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("FanControl.Runtime."+Names[i])){
                    if(resource==null || Hash(resource)!=Hashes[i])throw new InvalidOperationException("内嵌模块校验失败，请重新下载官方发布包。");
                    resource.Position=0;string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
                    try{
                        using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))resource.CopyTo(output);
                        // Another instance may have prepared the exact same resource concurrently.
                        if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);
                    }finally{if(File.Exists(temporary))File.Delete(temporary);}
                }
            }
            foreach(string name in new string[]{"LICENSE","THIRD-PARTY-NOTICES.md"}){
                using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("FanControl.Notice."+name)){
                    if(resource==null)throw new InvalidOperationException("发布文件缺少许可证。");
                    string path=Path.Combine(directory,name);string expected=Hash(resource);resource.Position=0;
                    if(!Matches(path,expected))using(var output=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))resource.CopyTo(output);
                }
            }
            Verify(directory);
        }
        public static void Verify(string directory){
            for(int i=0;i<Names.Length;i++)if(!Matches(Path.Combine(directory,Names[i]),Hashes[i]))
                throw new InvalidOperationException("运行模块不完整或校验失败，请重新连接以修复。");
        }
    }
}
