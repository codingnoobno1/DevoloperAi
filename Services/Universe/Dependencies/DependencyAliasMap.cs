using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.Universe.Dependencies
{
    /// <summary>
    /// The curated import-module → install-package translation (runtime.md §7b). This is the safety
    /// core of auto-install: we resolve a used import to a package via THIS table or an exact
    /// same-name match, never by trusting an arbitrary parsed string. Aliases (where the imported
    /// module name differs from the pip/npm package) are the dangerous cases, so they're enumerated
    /// explicitly. Standard-library / built-in modules are filtered out entirely.
    /// </summary>
    public static class DependencyAliasMap
    {
        /// <summary>Python import module → pip package, for the cases where they differ.</summary>
        public static readonly IReadOnlyDictionary<string, string> Python = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cv2"] = "opencv-python",
            ["sklearn"] = "scikit-learn",
            ["skimage"] = "scikit-image",
            ["PIL"] = "pillow",
            ["bs4"] = "beautifulsoup4",
            ["yaml"] = "pyyaml",
            ["dotenv"] = "python-dotenv",
            ["jwt"] = "pyjwt",
            ["dateutil"] = "python-dateutil",
            ["Crypto"] = "pycryptodome",
            ["OpenSSL"] = "pyopenssl",
            ["serial"] = "pyserial",
            ["win32api"] = "pywin32",
            ["win32com"] = "pywin32",
            ["google"] = "google-api-python-client",
            ["dns"] = "dnspython",
            ["Xlib"] = "python-xlib",
            ["usb"] = "pyusb",
            ["magic"] = "python-magic",
            ["fitz"] = "pymupdf",
            ["docx"] = "python-docx",
            ["pptx"] = "python-pptx",
            ["slugify"] = "python-slugify",
        };

        /// <summary>Python standard-library modules that must never be treated as installable.</summary>
        public static readonly HashSet<string> PythonStdlib = new(StringComparer.OrdinalIgnoreCase)
        {
            "os", "sys", "re", "json", "math", "typing", "datetime", "collections", "itertools",
            "functools", "asyncio", "subprocess", "threading", "multiprocessing", "pathlib", "io",
            "time", "random", "string", "logging", "argparse", "abc", "enum", "dataclasses",
            "contextlib", "copy", "hashlib", "base64", "uuid", "socket", "struct", "sqlite3",
            "unittest", "http", "urllib", "email", "csv", "glob", "shutil", "tempfile", "warnings",
            "traceback", "inspect", "importlib", "pickle", "queue", "signal", "operator", "decimal",
            "fractions", "statistics", "secrets", "gc", "weakref", "types", "builtins", "ast",
            "textwrap", "difflib", "bisect", "heapq", "array", "zlib", "gzip", "tarfile", "zipfile",
            "concurrent", "ctypes", "platform", "getpass", "webbrowser", "xml", "html", "ssl", "select",
        };

        /// <summary>Node.js built-in modules that must never be treated as installable.</summary>
        public static readonly HashSet<string> NodeBuiltins = new(StringComparer.OrdinalIgnoreCase)
        {
            "fs", "path", "http", "https", "os", "crypto", "util", "events", "stream", "url",
            "querystring", "child_process", "net", "zlib", "buffer", "process", "assert", "tls",
            "dns", "dgram", "readline", "repl", "vm", "cluster", "module", "timers", "console",
            "string_decoder", "perf_hooks", "worker_threads", "async_hooks", "diagnostics_channel",
            "fs/promises", "stream/promises", "timers/promises",
        };

        /// <summary>Resolve a Python import module to its pip package (curated alias or same name).</summary>
        public static (string package, string source)? ResolvePython(string module)
        {
            if (PythonStdlib.Contains(module)) return null;
            if (Python.TryGetValue(module, out var pkg)) return (pkg, "curated");
            return (module, "same-name");
        }

        /// <summary>Resolve a JS import spec to its npm package (scope-aware, subpath-stripped).</summary>
        public static (string package, string source)? ResolveJs(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            if (spec.StartsWith('.') || spec.StartsWith('/')) return null; // relative — local file

            string pkg;
            if (spec.StartsWith('@'))
            {
                var parts = spec.Split('/');
                pkg = parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : spec; // @scope/name
            }
            else
            {
                pkg = spec.Split('/')[0]; // strip subpath, e.g. date-fns/format → date-fns
            }

            if (NodeBuiltins.Contains(pkg)) return null;
            return (pkg, "same-name");
        }
    }
}
