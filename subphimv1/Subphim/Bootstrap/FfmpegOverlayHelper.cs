using subphimv1.Models;
using System;

namespace subphimv1.Services
{
    public static class FfmpegOverlayHelper
    {

        public static (int X, int Y, int W, int H) ComputeImageOnVideoPx(
            ProjectState proj,
            subphimv1.Subphim.MediaAsset asset)
        {
            double Vw = proj.ProjectReferenceVideoWidth;
            double Vh = proj.ProjectReferenceVideoHeight;
            if (Vw <= 0 || Vh <= 0 || asset.Width <= 0 || asset.Height <= 0)
            {
                return (0, 0, 0, 0);
            }
            double imgAR = (double)asset.Width / asset.Height;
            double baseW, baseH;

            if ((Vw / Vh) > imgAR)
            {
                baseH = Vh;
                baseW = baseH * imgAR;
            }

            else
            {
                baseW = Vw;
                baseH = baseW / imgAR;
            }
            double finalW = baseW * asset.ScaleX;
            double finalH = baseH * asset.ScaleY;
            double cx = asset.PositionX * Vw;
            double cy = asset.PositionY * Vh;
            double left = cx - finalW / 2.0;
            double top = cy - finalH / 2.0;
            int W_out = Math.Max(1, (int)Math.Round(finalW));
            int H_out = Math.Max(1, (int)Math.Round(finalH));
            int X_out = (int)Math.Round(left);
            int Y_out = (int)Math.Round(top);

            return (X_out, Y_out, W_out, H_out);
        }
    }
}