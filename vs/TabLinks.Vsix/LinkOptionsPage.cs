using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace TabLinks.Vsix
{
    [ComVisible(true)]
    [Guid("c4f8a2d6-1b37-4e90-8a5c-6d2e9f0b7143")]
    public sealed class LinkOptionsPage : DialogPage
    {
        [Category("Declarations")]
        [DisplayName("Implementation to interface")]
        [Description("On a class or member, show a link to the interface, base class or overridden member.")]
        public bool Implements { get; set; } = true;

        [Category("Declarations")]
        [DisplayName("Interface to implementations")]
        [Description("On an interface or base member, show links to the types and members that implement or override it.")]
        public bool ImplementedBy { get; set; } = true;

        [Category("Where it is used")]
        [DisplayName("Use of an object (type)")]
        [Description("At a place that uses a type, such as new Foo() or a parameter, show a link to the tab of that type.")]
        public bool UsesTypes { get; set; } = true;

        [Category("Where it is used")]
        [DisplayName("Use of a member (call, property)")]
        [Description("At a place that calls a method or reads a property of another tab, show a link to its declaration.")]
        public bool UsesMembers { get; set; } = true;

        [Category("Back links")]
        [DisplayName("Type to the places that use it")]
        [Description("On a type declaration, show links back to the tabs that use it.")]
        public bool UsedByTypes { get; set; } = true;

        [Category("Back links")]
        [DisplayName("Member to the places that use it")]
        [Description("On a method or property, show links back to the tabs that use it.")]
        public bool UsedByMembers { get; set; } = true;

        public override void LoadSettingsFromStorage()
        {
            var s = LinkSettings.Current;
            Implements = s.Implements; ImplementedBy = s.ImplementedBy;
            UsesTypes = s.UsesTypes; UsesMembers = s.UsesMembers;
            UsedByTypes = s.UsedByTypes; UsedByMembers = s.UsedByMembers;
        }

        public override void SaveSettingsToStorage()
        {
            LinkSettings.Save(new LinkSettings
            {
                Implements = Implements, ImplementedBy = ImplementedBy,
                UsesTypes = UsesTypes, UsesMembers = UsesMembers,
                UsedByTypes = UsedByTypes, UsedByMembers = UsedByMembers,
            });
            LinkService.Schedule(0);
        }
    }
}
