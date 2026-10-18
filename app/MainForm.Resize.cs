using System;
using System.Drawing;
using System.Windows.Forms;
namespace RedmiFanControl {
    public sealed partial class MainForm {
        Panel viewport,fanSection,temperatureSection,controlSection,rulesSection;
        float layoutScale;bool arrangingContent;
        static void AnchorMany(AnchorStyles anchors,params Control[] controls){foreach(var control in controls)control.Anchor=anchors;}
        int LayoutPixels(int value){return (int)Math.Round(value*layoutScale);}
        void InitializeResponsiveLayout(){
            using(var graphics=CreateGraphics())layoutScale=graphics.DpiX/96f;
            viewport.AutoScrollMinSize=new Size(LayoutPixels(920),LayoutPixels(700));
            viewport.ClientSizeChanged+=delegate{ArrangeContent();};
            ArrangeContent();
        }
        void ArrangeContent(){
            if(arrangingContent || IsDisposed || layoutScale==0)return;
            arrangingContent=true;
            try{
                // Scrollbars can reduce ClientSize again. Settle both axes without
                // changing the user's scroll position or scaling fonts on resize.
                for(int pass=0;pass<3;pass++){
                    int width=Math.Max(LayoutPixels(920),viewport.ClientSize.Width);
                    int height=Math.Max(LayoutPixels(700),viewport.ClientSize.Height);
                    canvas.SuspendLayout();
                    try{
                        canvas.Size=new Size(width,height);
                        int leftWidth=LayoutPixels(560)+(int)Math.Round((width-LayoutPixels(920))*0.65);
                        int rightX=LayoutPixels(36)+leftWidth,rightWidth=width-LayoutPixels(20)-rightX;
                        fanSection.SetBounds(LayoutPixels(20),LayoutPixels(176),leftWidth,LayoutPixels(160));
                        temperatureSection.SetBounds(rightX,LayoutPixels(176),rightWidth,LayoutPixels(160));
                        controlSection.SetBounds(LayoutPixels(20),LayoutPixels(350),leftWidth,height-LayoutPixels(458));
                        rulesSection.SetBounds(rightX,LayoutPixels(350),rightWidth,height-LayoutPixels(458));
                    }finally{canvas.ResumeLayout(true);}
                    viewport.PerformLayout();
                    if(canvas.Width==Math.Max(LayoutPixels(920),viewport.ClientSize.Width) && canvas.Height==Math.Max(LayoutPixels(700),viewport.ClientSize.Height))break;
                }
            }finally{arrangingContent=false;}
        }
    }
}
