using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace GeminiLauncher
{
    internal sealed class LaunchTarget
    {
        internal string Id, Shortcut, Executable;
    }

    internal sealed class LaunchService
    {
        private readonly Dictionary<string, LaunchTarget> targets;
        private readonly Action<ProcessStartInfo> start;

        internal LaunchService(string directory, Action<ProcessStartInfo> start)
        {
            this.start = start;
            targets = new Dictionary<string, LaunchTarget>(StringComparer.Ordinal);
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(Path.Combine(directory, "targets.xml"), settings)) document.Load(reader);
            foreach (XmlNode node in document.SelectNodes("/targets/app"))
            {
                string id = Required(node, "id");
                if ((id != "gemini" && id != "antigravity") || targets.ContainsKey(id))
                    throw new InvalidDataException("启动配置包含未知或重复的应用。");
                string link = Required(node, "shortcut");
                // Only the two installed copies can be launched; never follow the desktop entry recursively.
                if (link != id + ".lnk") throw new InvalidDataException("启动快捷方式名称无效。");
                string executable = Required(node, "executable");
                if (!Path.IsPathRooted(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("应用目标必须是完整的 EXE 路径。");
                targets.Add(id, new LaunchTarget { Id = id, Shortcut = Path.Combine(directory, "targets", link), Executable = executable });
            }
            if (targets.Count != 2) throw new InvalidDataException("启动配置需要 Gemini 和 Antigravity 两个选项。");
        }

        private static string Required(XmlNode node, string name)
        {
            var value = node.Attributes[name];
            if (value == null || string.IsNullOrWhiteSpace(value.Value)) throw new InvalidDataException("启动配置缺少 " + name + "。");
            return value.Value;
        }

        internal void Launch(string id)
        {
            LaunchTarget target;
            if (!targets.TryGetValue(id, out target)) throw new InvalidDataException("未知的应用选项。");
            if (!File.Exists(target.Executable)) throw new FileNotFoundException("找不到应用，请确认安装位置：\n" + target.Executable);
            if (!File.Exists(target.Shortcut)) throw new FileNotFoundException("启动入口丢失，请重新安装选择器。\n" + target.Shortcut);
            start(new ProcessStartInfo(target.Shortcut) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target.Executable) });
        }
    }
}
