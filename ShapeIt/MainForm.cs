using CADability.Forms;
using CADability;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
using System.Xml;
using CADability.UserInterface;
using System.Runtime.InteropServices;
using static ShapeIt.MainForm;

namespace ShapeIt
{
    public partial class MainForm : CadForm
    {
        public MainForm(string[] args) : base(args)
        {   // interpret the command line arguments as a name of a file, which should be opened
            string fileName = "";
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("-"))
                {
                    fileName = args[i];
                    break;
                }
            }
            Project toOpen = null;
            if (!String.IsNullOrWhiteSpace(fileName))
            {
                try
                {
                    toOpen = Project.ReadFromFile(fileName);
                }
                catch { }
            }
            if (toOpen == null) CadFrame.GenerateNewProject();
            else CadFrame.Project = toOpen;
            this.Text = "ShapeIt with CADability";
            //this.Width = 1280; //1266
            //this.Height= 720;//733
            //this.SetBounds(
            //    this.Left, this.Top,
            //    1280, 720,
            //    BoundsSpecified.Size); 
            bool exp = Settings.GlobalSettings.GetBoolValue("Experimental.TestNewContextMenu", false);
            bool tst = Settings.GlobalSettings.GetBoolValue("ShapeIt.Initialized", false);
            Settings.GlobalSettings.SetValue("ShapeIt.Initialized", true);
            CadFrame.FileNameChangedEvent += (name) => 
            {
                if (string.IsNullOrEmpty(name)) this.Text = "ShapeIt with CADability";
                else this.Text = "ShapeIt -- " + name;
            };
            CadFrame.ControlCenter.RemovePropertyPage("View");

            Assembly ThisAssembly = Assembly.GetExecutingAssembly();
            using (System.IO.Stream str = ThisAssembly.GetManifestResourceStream("ShapeIt.MenuResource.xml"))
            {
                XmlDocument menuDocument = new XmlDocument();
                menuDocument.Load(str);
                MenuResource.SetMenuResource(menuDocument);
                ResetMainMenu(null);
            }
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Setze hier die exakte Fenstergröße (inklusive Rahmen) auf 1280x720
            this.Size = new Size(1294, 727);

        }

        public override bool OnCommand(string MenuId)
        {
            if (MenuId == "MenuId.App.Exit")
            {   // this command cannot be handled by CADability.dll
#if DEBUG
                System.GC.Collect();
                System.GC.WaitForFullGCComplete();
                System.GC.Collect();
#endif
                Application.Exit();
                return true;
            }
            else return base.OnCommand(MenuId);
        }
    }
}
