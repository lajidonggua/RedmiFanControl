using System;
using System.Text;
namespace RedmiFanControl {
    static class CurveFormat {
        public static CurvePoint[] Parse(string text){
            if(text==null)throw new ArgumentException("曲线不能为空。");
            string[] parts=text.Split(new char[]{';','；'},StringSplitOptions.RemoveEmptyEntries);var points=new CurvePoint[parts.Length];
            for(int i=0;i<parts.Length;i++){
                string[] pair=parts[i].Split(':','：');int temperature,percent;
                if(pair.Length!=2 || !int.TryParse(pair[0].Trim(),out temperature) || !int.TryParse(pair[1].Trim(),out percent))throw new ArgumentException("请填写有效的温度和输出节点。");
                points[i]=new CurvePoint{Temperature=temperature,Percent=percent};
            }
            return points;
        }
        public static string Format(CurvePoint[] points){
            var text=new StringBuilder();foreach(var p in points){if(text.Length>0)text.Append("; ");text.Append(p.Temperature).Append(':').Append(p.Percent);}return text.ToString();
        }
    }
}
