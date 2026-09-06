using UnityEditor;
using UnityEditor.Callbacks;

#if UNITY_IOS
using UnityEditor.iOS.Xcode;
using System.IO;
#endif

namespace GameLift.Editor
{
    public class IOSPostProcessBuild
    {
        [PostProcessBuild(900)]
        public static void OnPostProcessBuild(BuildTarget buildTarget, string pathToBuiltProject)
        {
#if UNITY_IOS
            if (buildTarget == BuildTarget.iOS)
            {
                // Get the Info.plist file path
                string plistPath = pathToBuiltProject + "/Info.plist";
                
                // Read the Plist
                PlistDocument plist = new PlistDocument();
                plist.ReadFromString(System.IO.File.ReadAllText(plistPath));

                // Get root
                PlistElementDict rootDict = plist.root;

                // Add or update the App Tracking Transparency description
                rootDict.SetString("NSUserTrackingUsageDescription", "This identifier will be used to deliver personalized ads to you.");

                // Write it back to the file
                System.IO.File.WriteAllText(plistPath, plist.WriteToString());
            }
#endif
        }
    }
}
