namespace ApirApir.AugmentedReality
{
    /// <summary>
    /// Build-time status and dependency names for the official Unity AR integration.
    /// The explicit symbol prevents missing optional packages from breaking non-AR builds.
    /// </summary>
    public static class ARFoundationIntegrationStatus
    {
        public const string RequiredScriptingDefine = "APIRAPIR_ARFOUNDATION";
        public const string ARFoundationPackage = "com.unity.xr.arfoundation";
        public const string XRPluginManagementPackage = "com.unity.xr.management";
        public const string AndroidProviderPackage = "com.unity.xr.arcore";
        public const string OptionalIosProviderPackage = "com.unity.xr.arkit";

#if APIRAPIR_ARFOUNDATION
        public const bool IsCompiled = true;
#else
        public const bool IsCompiled = false;
#endif
    }
}
