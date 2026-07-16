namespace RakionServer.Common;

public readonly record struct LauncherBuildIdentity(int AppId, int BuildVersion)
{
    public bool IsSpecified => AppId > 0 && BuildVersion >= 0;
}
