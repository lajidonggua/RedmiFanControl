param()
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$iconSource=@'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class FanIconAssets {
    public static Bitmap Render(int size) {
        int renderSize=Math.Max(256,size*4);
        using(var source=new Bitmap(renderSize,renderSize,PixelFormat.Format32bppArgb)) {
            using(var g=Graphics.FromImage(source)) {
                g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);g.ScaleTransform(renderSize/256f,renderSize/256f);
                using(var shape=new GraphicsPath()) {
                    shape.AddArc(12,12,104,104,180,90);shape.AddArc(140,12,104,104,270,90);
                    shape.AddArc(140,140,104,104,0,90);shape.AddArc(12,140,104,104,90,90);shape.CloseFigure();
                    using(var fill=new LinearGradientBrush(new Point(30,20),new Point(225,244),Color.FromArgb(36,142,225),Color.FromArgb(22,55,126)))g.FillPath(fill,shape);
                }
                using(var ring=new Pen(Color.FromArgb(45,255,255,255),3))g.DrawEllipse(ring,44,44,168,168);
                for(int i=0;i<4;i++) {
                    var state=g.Save();g.TranslateTransform(128,128);g.RotateTransform(90*i);g.TranslateTransform(-128,-128);
                    using(var blade=new GraphicsPath()) {
                        blade.AddBezier(126,109,139,67,171,57,188,78);
                        blade.AddBezier(188,78,218,115,178,141,145,131);
                        blade.AddBezier(145,131,135,127,128,119,126,109);blade.CloseFigure();
                        using(var fill=new SolidBrush(Color.FromArgb(248,253,255)))g.FillPath(fill,blade);
                    }g.Restore(state);
                }
                using(var hub=new SolidBrush(Color.FromArgb(88,231,211)))g.FillEllipse(hub,110,110,36,36);
                using(var center=new SolidBrush(Color.FromArgb(21,77,133)))g.FillEllipse(center,122,122,12,12);
            }
            var result=new Bitmap(size,size,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(result)){g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.DrawImage(source,new Rectangle(0,0,size,size));}
            return result;
        }
    }
    public static void Generate(string folder) {
        int[] sizes={16,20,24,32,48,64,128,256};byte[][] images=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++)using(var bmp=Render(sizes[i]))using(var buffer=new MemoryStream()) {
            bmp.Save(buffer,ImageFormat.Png);images[i]=buffer.ToArray();
            if(sizes[i]==256)File.WriteAllBytes(Path.Combine(folder,"fan.png"),images[i]);
        }
        using(var file=new BinaryWriter(File.Create(Path.Combine(folder,"fan.ico")))) {
            file.Write((ushort)0);file.Write((ushort)1);file.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) {
                file.Write((byte)(sizes[i]==256?0:sizes[i]));file.Write((byte)(sizes[i]==256?0:sizes[i]));file.Write((byte)0);file.Write((byte)0);
                file.Write((ushort)1);file.Write((ushort)32);file.Write(images[i].Length);file.Write(offset);offset+=images[i].Length;
            }
            foreach(var bytes in images)file.Write(bytes);
        }
    }
}
'@
Add-Type -TypeDefinition $iconSource -ReferencedAssemblies System.Drawing
[FanIconAssets]::Generate($PSScriptRoot)
Write-Output 'Generated original multi-resolution fan icon (16-256 px).'
