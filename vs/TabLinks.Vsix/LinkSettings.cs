using System;
using System.IO;
using System.Linq;

namespace TabLinks.Vsix
{
    internal enum LinkKind { Implements, ImplementedBy, UsesType, UsesMember, UsedByType, UsedByMember }

    /// <summary>Which links to show. Stored in a text file, so the editor can read it before the package loads.</summary>
    internal sealed class LinkSettings
    {
        public bool Implements = true;
        public bool ImplementedBy = true;
        public bool UsesTypes = true;
        public bool UsesMembers = true;
        public bool UsedByTypes = true;
        public bool UsedByMembers = true;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabLinks", "settings.txt");

        private static LinkSettings? _current;
        public static LinkSettings Current => _current ??= Load();

        public bool Allows(LinkKind kind)
        {
            switch (kind)
            {
                case LinkKind.Implements: return Implements;
                case LinkKind.ImplementedBy: return ImplementedBy;
                case LinkKind.UsesType: return UsesTypes;
                case LinkKind.UsesMember: return UsesMembers;
                case LinkKind.UsedByType: return UsedByTypes;
                default: return UsedByMembers;
            }
        }

        public bool AnyUsage => UsesTypes || UsesMembers || UsedByTypes || UsedByMembers;
        public bool AnyBase => Implements || ImplementedBy;

        public static LinkSettings Load()
        {
            var s = new LinkSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2 || !bool.TryParse(parts[1].Trim(), out var v)) continue;
                    switch (parts[0].Trim())
                    {
                        case nameof(Implements): s.Implements = v; break;
                        case nameof(ImplementedBy): s.ImplementedBy = v; break;
                        case nameof(UsesTypes): s.UsesTypes = v; break;
                        case nameof(UsesMembers): s.UsesMembers = v; break;
                        case nameof(UsedByTypes): s.UsedByTypes = v; break;
                        case nameof(UsedByMembers): s.UsedByMembers = v; break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return s;
        }

        public static void Save(LinkSettings s)
        {
            _current = s;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllLines(FilePath, new[]
                {
                    "Implements=" + s.Implements, "ImplementedBy=" + s.ImplementedBy,
                    "UsesTypes=" + s.UsesTypes, "UsesMembers=" + s.UsesMembers,
                    "UsedByTypes=" + s.UsedByTypes, "UsedByMembers=" + s.UsedByMembers,
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
