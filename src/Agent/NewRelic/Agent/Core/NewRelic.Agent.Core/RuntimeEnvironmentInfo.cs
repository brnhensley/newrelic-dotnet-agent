// Copyright 2020 New Relic, Inc. All rights reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;

namespace NewRelic.Agent.Core
{
    // Originally sourced from .NET SDK (https://github.com/dotnet/sdk)
    // https://github.com/dotnet/sdk/blob/3595e2a/src/Cli/Microsoft.DotNet.Cli.Utils/RuntimeEnvironment.cs
    // Copyright (c) .NET Foundation and contributors. All rights reserved.
    // Licensed under the MIT license. See LICENSE file in the project root for full license information.
    //
    // Modifications Copyright (c) New Relic, Inc.
    //
    // * Modifications include renaming the class and the addition of exception handling and logging.

    public static class RuntimeEnvironmentInfo
    {
        private enum Platform
        {
            Unknown = 0,
            Windows = 1,
            Linux = 2,
            Darwin = 3,
            FreeBSD = 4
        }

        private static readonly Lazy<Platform> _platform = new Lazy<Platform>(DetermineOSPlatform);
        private static readonly Lazy<DistroInfo> _distroInfo = new Lazy<DistroInfo>(LoadDistroInfo);

        public static string OperatingSystemVersion { get; } = GetOSVersion();
        public static string OperatingSystem { get; } = GetOSName();

        private class DistroInfo
        {
            public string Id;
            public string VersionId;
        }

        private static string GetOSName()
        {
            switch (GetOSPlatform())
            {
                case Platform.Windows:
                    return "Windows";
                case Platform.Linux:
                    return GetDistroId() ?? "Linux";
                case Platform.Darwin:
                    return "Mac OS X";
                case Platform.FreeBSD:
                    return "FreeBSD";
                default:
                    return "Unknown";
            }
        }

        private static string GetOSVersion()
        {
            switch (GetOSPlatform())
            {
                case Platform.Windows:
                    return System.Environment.OSVersion.Version.ToString(3);
                case Platform.Linux:
                    return GetDistroVersionId() ?? string.Empty;
                case Platform.Darwin:
                    return System.Environment.OSVersion.Version.ToString(2);
                case Platform.FreeBSD:
                    return GetFreeBSDVersion() ?? string.Empty;
                default:
                    return string.Empty;
            }
        }

        private static string GetFreeBSDVersion()
        {
#if NETSTANDARD2_0
            // This is same as sysctl kern.version
            // FreeBSD 11.0-RELEASE-p1 FreeBSD 11.0-RELEASE-p1 #0 r306420: Thu Sep 29 01:43:23 UTC 2016     root@releng2.nyi.freebsd.org:/usr/obj/usr/src/sys/GENERIC
            // What we want is major release as minor releases should be compatible.
            String version = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
            try
            {
                // second token up to first dot
                return System.Runtime.InteropServices.RuntimeInformation.OSDescription.Split()[1].Split('.')[0];
            }
            catch (Exception ex)
            {
                log4net.ILog logger = log4net.LogManager.GetLogger(typeof(RuntimeEnvironmentInfo));
                logger.Debug($"Unable to report Operating System: Unexpected exception in GetFreeBSDVersion: {ex}");
            }
#endif
            return string.Empty;
        }

        private static Platform GetOSPlatform()
        {
            return _platform.Value;
        }

        private static string GetDistroId()
        {
            return _distroInfo.Value?.Id;
        }

        private static string GetDistroVersionId()
        {
            return _distroInfo.Value?.VersionId;
        }

        private static DistroInfo LoadDistroInfo()
        {
            DistroInfo result = null;

            // Sample os-release file:
            //   NAME="Ubuntu"
            //   VERSION = "14.04.3 LTS, Trusty Tahr"
            //   ID = ubuntu
            //   ID_LIKE = debian
            //   PRETTY_NAME = "Ubuntu 14.04.3 LTS"
            //   VERSION_ID = "14.04"
            //   HOME_URL = "http://www.ubuntu.com/"
            //   SUPPORT_URL = "http://help.ubuntu.com/"
            //   BUG_REPORT_URL = "http://bugs.launchpad.net/ubuntu/"
            // We use ID and VERSION_ID

            try
            {
                if (File.Exists("/etc/os-release"))
                {
                    var lines = File.ReadAllLines("/etc/os-release");
                    result = new DistroInfo();
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("ID=", StringComparison.Ordinal))
                        {
                            result.Id = line.Substring(3).Trim('"', '\'');
                        }
                        else if (line.StartsWith("VERSION_ID=", StringComparison.Ordinal))
                        {
                            result.VersionId = line.Substring(11).Trim('"', '\'');
                        }
                    }
                }

                if (result != null)
                {
                    result = NormalizeDistroInfo(result);
                }
            }
            catch (Exception ex)
            {
                log4net.ILog logger = log4net.LogManager.GetLogger(typeof(RuntimeEnvironmentInfo));
                logger.Debug($"Unable to report Operating System: Unexpected exception in LoadDistroInfo: {ex}");
            }

            return result;
        }

        // For some distros, we don't want to use the full version from VERSION_ID. One example is
        // Red Hat Enterprise Linux, which includes a minor version in their VERSION_ID but minor
        // versions are backwards compatable.
        //
        // In this case, we'll normalized RIDs like 'rhel.7.2' and 'rhel.7.3' to a generic
        // 'rhel.7'. This brings RHEL in line with other distros like CentOS or Debian which
        // don't put minor version numbers in their VERSION_ID fields because all minor versions
        // are backwards compatible.
        private static DistroInfo NormalizeDistroInfo(DistroInfo distroInfo)
        {
            // Handle if VersionId is null by just setting the index to -1.
            int lastVersionNumberSeparatorIndex = distroInfo.VersionId?.IndexOf('.') ?? -1;

            if (lastVersionNumberSeparatorIndex != -1 && distroInfo.Id == "alpine")
            {
                // For Alpine, the version reported has three components, so we need to find the second version separator
                lastVersionNumberSeparatorIndex = distroInfo.VersionId.IndexOf('.', lastVersionNumberSeparatorIndex + 1);
            }

            if (lastVersionNumberSeparatorIndex != -1 && (distroInfo.Id == "rhel" || distroInfo.Id == "alpine"))
            {
                distroInfo.VersionId = distroInfo.VersionId.Substring(0, lastVersionNumberSeparatorIndex);
            }

            return distroInfo;
        }

        private static Platform DetermineOSPlatform()
        {
#if NETSTANDARD2_0
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return Platform.Windows;
            }
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                return Platform.Linux;
            }
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                return Platform.Darwin;
            }
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Create("FREEBSD")))
            {
                return Platform.FreeBSD;
            }

            return Platform.Unknown;
#else
            return Platform.Windows;
#endif
        }
    }
}
