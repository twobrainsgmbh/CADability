using CADability.GeoObject;
using CADability;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using CADability.Attribute;

namespace ShapeIt
{
    internal class Feedback
    {
        private IView view;
        public GeoObjectList FrontFaces = new GeoObjectList(); // List of front faces for distance for feedback
        public GeoObjectList BackFaces = new GeoObjectList(); // List of back faces for distance for feedback
        public GeoObjectList SelectedObjects = new GeoObjectList(); // List of selected objects to be displayed, when a entry is selected
        public GeoObjectList Arrows = new GeoObjectList(); // List of highlighted objects to be displayed, when a entry is selected
        private IPaintTo3DList frontFacesDisplayList = null;
        private IPaintTo3DList backFacesDisplayList = null;
        private IPaintTo3DList selectedObjectsDisplayList = null;
        private IPaintTo3DList arrowsDisplayList = null;

        ColorDef frontColor, backColor, selectColor;
        public Feedback()
        {
            frontColor = new ColorDef("frontColor", Color.Yellow);
            backColor = new ColorDef("backColor", Color.LightBlue);
            selectColor = new ColorDef("frontColor", Color.LightPink);
        }
        public void Attach(IView vw)
        {
            if (view != null) Detach();
            view = vw;
            vw.SetPaintHandler(PaintBuffer.DrawingAspect.Select, OnRepaint);
        }
        public void Detach()
        {
            view.RemovePaintHandler(PaintBuffer.DrawingAspect.Select, OnRepaint);
        }

        public void Clear()
        {
            FrontFaces.Clear();
            BackFaces.Clear();
            SelectedObjects.Clear();
            Arrows.Clear();
            // reset the display lists, so they will be recreated when the next OnRepaintSelect is called
            frontFacesDisplayList = null;
            backFacesDisplayList = null;
            selectedObjectsDisplayList = null;
            arrowsDisplayList = null;
        }


        private void OnRepaint(Rectangle IsInvalid, IView view, IPaintTo3D PaintToSelect)
        {
            // save the state of PaintToSelect
            PaintToSelect.PushState();
            bool oldSelect = PaintToSelect.SelectMode;
            bool pse = PaintToSelect.PaintSurfaceEdges;

            // if the display lists are null, regenerate them
            PaintToSelect.SelectMode = true;
            PaintToSelect.SelectColor = Color.Yellow; //Color.FromArgb(196, Color.Yellow);
            PaintToSelect.SetColor(Color.Yellow);
            PaintToSelect.PaintSurfaceEdges = false;

            ModOp toViewer = ModOp.Translate(-2*PaintToSelect.Precision * view.Projection.Direction.x, -2 * PaintToSelect.Precision * view.Projection.Direction.y, -2 * PaintToSelect.Precision * view.Projection.Direction.z);
            PaintToSelect.PushMultModOp(toViewer);

            if (selectedObjectsDisplayList == null)
            {
                PaintToSelect.OpenList("selected-objects");
                foreach (IGeoObject go in SelectedObjects)
                {
                    ColorDef oldcd = (go as IColorDef).ColorDef;
                    (go as IColorDef).ColorDef = selectColor;
                    go.PaintTo3D(PaintToSelect);
                    (go as IColorDef).ColorDef = oldcd;
                }
                selectedObjectsDisplayList = PaintToSelect.CloseList();
            }
            PaintToSelect.SelectMode = false;
            // front and back faces list in plain mode
            PaintToSelect.SetColor(Color.Yellow);
            if (frontFacesDisplayList == null)
            {
                PaintToSelect.OpenList("front-faces");
                foreach (IGeoObject go in FrontFaces)
                {
                    ColorDef oldcd = (go as IColorDef).ColorDef;
                    (go as IColorDef).ColorDef = frontColor;
                    go.PaintTo3D(PaintToSelect);
                    (go as IColorDef).ColorDef = oldcd;
                }
                frontFacesDisplayList = PaintToSelect.CloseList();
            }
            PaintToSelect.SetColor(Color.LightBlue);
            if (backFacesDisplayList == null)
            {
                PaintToSelect.OpenList("back-faces");
                foreach (IGeoObject go in BackFaces)
                {
                    ColorDef oldcd = (go as IColorDef).ColorDef;
                    (go as IColorDef).ColorDef = backColor;
                    go.PaintTo3D(PaintToSelect);
                    (go as IColorDef).ColorDef = oldcd;
                }
                backFacesDisplayList = PaintToSelect.CloseList();
            }
            PaintToSelect.SetColor(Color.Yellow); // color to display the selected objects as plain normal faces or edges
            PaintToSelect.SetColor(Color.Black); // color to display the arrows an text. objects should have ColorDef==null, so they don't set the color
            bool oldTriangulateText = PaintToSelect.TriangulateText;
            PaintToSelect.TriangulateText = false;
            if (arrowsDisplayList == null)
            {
                PaintToSelect.OpenList("arrow-objects");
                foreach (IGeoObject go in Arrows)
                {
                    go.PaintTo3D(PaintToSelect);
                }
                arrowsDisplayList = PaintToSelect.CloseList();
            }

            // show the display lists            
            PaintToSelect.SelectMode = true;
            if (selectedObjectsDisplayList != null) PaintToSelect.SelectedList(selectedObjectsDisplayList, 6);// width of the brim
            PaintToSelect.SelectMode = false;
            PaintToSelect.SetColor(Color.Yellow);
            if (frontFacesDisplayList != null) PaintToSelect.List(frontFacesDisplayList);
            PaintToSelect.SetColor(Color.LightBlue);
            if (backFacesDisplayList != null) PaintToSelect.List(backFacesDisplayList);
            PaintToSelect.SetColor(Color.Black);
            if (arrowsDisplayList != null) PaintToSelect.List(arrowsDisplayList);

            // restore the state of PaintToSelect
            PaintToSelect.PopModOp();
            PaintToSelect.TriangulateText = oldTriangulateText;
            PaintToSelect.SelectMode = oldSelect;
            PaintToSelect.PaintSurfaceEdges = pse;
            PaintToSelect.PopState();
        }
    }
}
