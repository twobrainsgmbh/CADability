using CADability;
using CADability.UserInterface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeIt
{
    /// <summary>
    /// A simple line in the property grid of a property page. It shows the text of the resourceId which can be overwritten
    /// by setting the LabelText property. When selected or unselected <see cref="IsSelected"/> is beeing called, where you can
    /// give feedback in the current View. 
    /// </summary>
    internal class SelectEntry : SimplePropertyGroup
    {
        private PropertyEntryType flags;
        /// <summary>
        /// Marks this entry as selectable and having a directMenu button and behaviour
        /// </summary>
        public override PropertyEntryType Flags => flags;
        public SelectEntry(string resourceId, bool hasSubEntries = false) : base(resourceId)
        {
            if (hasSubEntries) flags = PropertyEntryType.Selectable | PropertyEntryType.GroupTitle | PropertyEntryType.HasSubEntries;
            else flags = PropertyEntryType.Selectable;
        }
        /// <summary>
        /// Supply your code here to react on selection and deselection of this entry
        /// </summary>
        public virtual Func<bool, IFrame, bool> IsSelected { get; set; } = (selected, frame) => false;
        /// <summary>
        /// Handles the selection of this entry and calls <see cref="IsSelected"/>(true).
        /// </summary>
        /// <param name="previousSelected"></param>
        public override void Selected(IPropertyEntry previousSelected)
        {
            IsSelected(true, Frame);
            base.Selected(previousSelected);
        }
        /// <summary>
        /// Handles the de-selection of this entry and calls <see cref="IsSelected"/>(false).
        /// </summary>
        /// <param name="nowSelected"></param>
        public override void UnSelected(IPropertyEntry nowSelected)
        {
            IsSelected(false, Frame);
            base.UnSelected(nowSelected);
        }
    }
    /// <summary>
    /// A simple line in the property grid of a property page. It shows the text of the resourceId which can be overwritten
    /// by setting the LabelText property. When selected or unselected <see cref="IsSelected"/> is beeing called, where you can
    /// give feedback in the current View. 
    /// It has a "directMenu" button attached to the line, which when pressed calls <see cref="ExecuteMenu"/>, where you 
    /// add your code. It als calls ExecuteMenu when the line is selected and enter is beeing pressed.
    /// </summary>
    internal class DirectMenuEntry : SelectEntry
    {
        /// <summary>
        /// Marks this entry as selectable and having a directMenu button and behaviour
        /// </summary>
        public override PropertyEntryType Flags => PropertyEntryType.Selectable | PropertyEntryType.DirectMenu;
        public DirectMenuEntry(string resourceId, bool useMenu = true) : base(resourceId)
        {
        }
        /// <summary>
        /// Supply your code here, which is executed when the menu button is clicked or enter is pressed
        /// </summary>
        public Func<IFrame, bool> ExecuteMenu { get; set; } = (frame) => false;
        /// <summary>
        /// Supply your code here to react on selection and deselection of this entry
        /// </summary>
        public override Func<bool, IFrame, bool> IsSelected { get; set; } = (selected, frame) => false;
        /// <summary>
        /// Handles the menu button click or enter and forwards it to <see cref="ExecuteMenu"/>
        /// </summary>
        /// <param name="button"></param>
        public override void ButtonClicked(PropertyEntryButton button)
        {
            if (Flags.HasFlag(PropertyEntryType.DirectMenu) || button == PropertyEntryButton.doubleclick) ExecuteMenu(Frame);
        }
    }
}