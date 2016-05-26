using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace HiDpiFixer
{
    public class Manifest
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string modulePath, IntPtr hFile, uint dwFlags);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EnumResourceNames(IntPtr hModule, IntPtr type, EnumResNameProc lpEnumFunc, int lParam);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr FindResource(IntPtr hModule, IntPtr type, IntPtr name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern ushort SizeofResource(IntPtr hModule, IntPtr hResInfo);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LockResource(IntPtr hResData);

        private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
        private static readonly IntPtr RT_MANIFEST = (IntPtr)24;

        private delegate bool EnumResNameProc(IntPtr hModule, IntPtr type, IntPtr name, IntPtr lp);

        private static readonly XmlNamespaceManager NamespaceManager;
        private string _path;
        private XDocument _xdocManifest;

        private static readonly XNamespace NsAsmV1 = "urn:schemas-microsoft-com:asm.v1";
        private static readonly XNamespace NsAsmV3 = "urn:schemas-microsoft-com:asm.v3";
        private static readonly XNamespace NsWindowsSettings = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
        private static readonly XNamespace NsHiDpiHelper = "urn:hidpi-helper";

        static Manifest()
        {
            NamespaceManager = new XmlNamespaceManager(new NameTable());
            NamespaceManager.AddNamespace("asmv1", NsAsmV1.NamespaceName);
            NamespaceManager.AddNamespace("asmv3", NsAsmV3.NamespaceName);
            NamespaceManager.AddNamespace("ws", NsWindowsSettings.NamespaceName);
            NamespaceManager.AddNamespace("hidpi", NsHiDpiHelper.NamespaceName);
        }
 
        public bool LoadFromExe(string path)
        {
            // Make sure the path can be loaded
            var moduleHandle = LoadLibraryEx(path, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
            if (moduleHandle == IntPtr.Zero)
                throw new Exception($"Unable to load module '{path}'");

            // Determine the manifest
            byte[] manifest = null;

            // Attempt to load the manifest
            EnumResourceNames(moduleHandle, RT_MANIFEST, (hMod, type, name, lp) =>
            {
                // Find resource
                var hResInfo = FindResource(hMod, name, type);

                // Determine the resource size
                var cbResource = SizeofResource(hMod, hResInfo);

                // Load and lock the resource
                var hResData = LoadResource(hMod, hResInfo);
                var pResource = LockResource(hResData);

                // Map the resource into a byte array
                manifest = new byte[cbResource];
                Marshal.Copy(pResource, manifest, 0, manifest.Length);
                return true;
            }, 0);

            // Abort if no manifest was loaded
            if (manifest == null)
                return false;

            // Create a memory stream based on the manifest
            using (var manifestStream = new MemoryStream(manifest, false))
            {
                // Save the manifest as an XML document
                _xdocManifest = XDocument.Load(manifestStream);
            }

            // Save path
            _path = path;

            // Manifest loaded
            return true;
        }

        public bool LoadFromManifest(string path)
        {
            // Load the manifest from the file
            using (var manifestStream = File.OpenRead(path))
            {
                _xdocManifest = XDocument.Load(manifestStream);
            }

            // Save the manifest
            _path = path;
            return true;
        }

        public void SaveManifest()
        {
            if (_xdocManifest != null)
            {
                // Determine the name of the manifest file
                var path = _path;
                if (!path.EndsWith(".manifest", StringComparison.InvariantCultureIgnoreCase))
                    path = path + ".manifest";

                // Write side-by-side manifest file
                using (var manifestStream = File.Create(path))
                using (var textWriter = new XmlTextWriter(manifestStream, Encoding.UTF8))
                {
                    _xdocManifest.Save(textWriter);
                }
            }
        }

        public bool? IsDpiAware
        {
            get
            {
                // Attempt to get the proper manifest
                if (_xdocManifest == null)
                    return null;

                var xDpiAware = GetDpiAwareNode();
                var dpiAware = xDpiAware != null ? (bool?) XmlConvert.ToBoolean(xDpiAware.Value) : null;
                return dpiAware;
            }

            set
            {
                // Check if the value changes
                if (IsDpiAware != value)
                {
                    if (!value.HasValue)
                    {
                        if (_xdocManifest != null)
                        {
                            // Remove the DPI aware flag
                            GetDpiAwareNode().Remove();
                        }
                    }
                    else
                    {
                        // Get the assembly element
                        var xAssembly = _xdocManifest.Root.XPathSelectElement("/asmv1:assembly", NamespaceManager);
                        if (xAssembly == null)
                        {
                            // Create a manifest document if none exists
                            _xdocManifest = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                                xAssembly = new XElement(NsAsmV1 + "assembly"));
                        }

                        // Get the application element
                        var xApplication = xAssembly.XPathSelectElement("./asmv3:application", NamespaceManager);
                        if (xApplication == null)
                            xAssembly.Add(xApplication = new XElement(NsAsmV3 + "application"));

                        // Get the application element
                        var xWindowsSettings = xApplication.XPathSelectElement("./asmv3:windowsSettings",
                            NamespaceManager);
                        if (xWindowsSettings == null)
                            xApplication.Add(xWindowsSettings = new XElement(NsAsmV3 + "windowsSettings"));

                        // Add or remove the flag
                        var xDpiAware = xWindowsSettings.XPathSelectElement("./ws:dpiAware", NamespaceManager);
                        if (xDpiAware == null)
                            xWindowsSettings.Add(xDpiAware = new XElement(NsWindowsSettings + "dpiAware"));

                        // Set the DPI aware flag
                        xDpiAware.Value = XmlConvert.ToString(value.Value);
                    }
                }
            }
        }

        private XElement GetDpiAwareNode()
        {
            // Attempt to find the proper item in the document
            return _xdocManifest.Root?.XPathSelectElement("/asmv1:assembly/asmv3:application/asmv3:windowsSettings/ws:dpiAware", NamespaceManager);
        }
    }
}
