using System.Drawing;
using System.Reflection;
namespace RedmiFanControl {
    static class AppBrand {
        public static Icon WindowIcon(){
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("FanControl.Icon"))
            using(var source=new Icon(stream,32,32))return (Icon)source.Clone();
        }
        public static Bitmap HeaderImage(){
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("FanControl.Image"))
            using(var source=new Bitmap(stream))return new Bitmap(source);
        }
    }
}
