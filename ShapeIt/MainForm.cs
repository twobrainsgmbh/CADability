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
using System.IO;
using CADability.Actions;

namespace ShapeIt
{
    public partial class MainForm : CadForm
    {
        private ModellingPropertyEntries modellingPropertyEntries;
        private DateTime lastSaved; // time, when the current file has been saved the last time, see OnIdle
        bool projectionChanged = false; // to handle projection changes in OnIdle
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
            bool exp = Settings.GlobalSettings.GetBoolValue("Experimental.TestNewContextMenu", false);
            bool tst = Settings.GlobalSettings.GetBoolValue("ShapeIt.Initialized", false);
            Settings.GlobalSettings.SetValue("ShapeIt.Initialized", true);
            CadFrame.FileNameChangedEvent += (name) =>
            {
                if (string.IsNullOrEmpty(name)) this.Text = "ShapeIt with CADability";
                else this.Text = "ShapeIt -- " + name;
                lastSaved = DateTime.Now; // a new file has been opened
            };
            CadFrame.ProjectClosedEvent += OnProjectClosed;
            CadFrame.ProjectOpenedEvent += OnProjectOpened;
            CadFrame.UIService.ApplicationIdle += OnIdle;
            CadFrame.ViewsChangedEvent += OnViewsChanged;
            CadFrame.ControlCenter.RemovePropertyPage("View");
            Assembly ThisAssembly = Assembly.GetExecutingAssembly();
            using (System.IO.Stream str = ThisAssembly.GetManifestResourceStream("ShapeIt.MenuResource.xml"))
            {
                XmlDocument menuDocument = new XmlDocument();
                menuDocument.Load(str);
                MenuResource.SetMenuResource(menuDocument);
                ResetMainMenu(null);
            }
            lastSaved = DateTime.Now;
            // the following installs the property page for modelling. This connects all modelling
            // tasks of ShapeIt with CADability
            IPropertyPage modellingPropPage = CadFrame.ControlCenter.AddPropertyPage("Modelling", 6);
            modellingPropertyEntries = new ModellingPropertyEntries(CadFrame);
            modellingPropPage.Add(modellingPropertyEntries, false);
            CadFrame.ControlCenter.ShowPropertyPage("Modelling");
        }

        private void OnViewsChanged(IFrame theFrame)
        {
            theFrame.ActiveView.Projection.ProjectionChangedEvent += OnProjectionChanged;
        }

        private void OnProjectionChanged(Projection sender, EventArgs args)
        {
            projectionChanged = true;
        }

        /// <summary>
        /// Filter the escape key for the modelling property page
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="keyData"></param>
        /// <returns></returns>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys nmKeyData = (Keys)((int)keyData & 0x0FFFF);
            CADability.Substitutes.KeyEventArgs e = new CADability.Substitutes.KeyEventArgs((CADability.Substitutes.Keys)keyData);
            if (nmKeyData == Keys.Escape)
            {
                if (modellingPropertyEntries.OnEscape()) return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        /// <summary>
        /// Called when CADability is idle. We use it to save the current project data to a temp file in case of a crash
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void OnIdle(object sender, EventArgs e)
        {
            if (projectionChanged)
            {
                projectionChanged = false;
                modellingPropertyEntries.OnProjectionChanged(); // to update the feedback objects, which are projection dependant
            }
            if (CadFrame.Project.IsModified && (DateTime.Now - lastSaved).TotalMinutes > 2)
            {
                CadFrame.Project.IsModified = false;
                string path = Path.GetTempPath();
                path = Path.Combine(path, "ShapeIt");
                DirectoryInfo dirInfo = Directory.CreateDirectory(path);
                string currentFileName = CadFrame.Project.FileName;
                if (string.IsNullOrEmpty(CadFrame.Project.FileName)) path = Path.Combine(path, "noname.cdb.json");
                else
                {
                    string fileName = Path.GetFileNameWithoutExtension(CadFrame.Project.FileName);
                    if (fileName.EndsWith(".cdb")) fileName = Path.GetFileNameWithoutExtension(fileName); // we usually have two extensions: .cdb.json
                    path = Path.Combine(path, fileName + "_.cdb.json");
                }
                CadFrame.Project.WriteToFile(path);
                CadFrame.Project.FileName = currentFileName; // Project.WriteToFile changes the Project.FileName, restore the current name
                lastSaved = DateTime.Now;
            }
        }
        void OnProjectClosed(Project theProject, IFrame theFrame)
        {
            // manage autosave OnIdle
        }
        private void OnProjectOpened(Project theProject, IFrame theFrame)
        {
            // manage autosave OnIdle
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // this is for recording the session with 1280x720 pixel. 
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
