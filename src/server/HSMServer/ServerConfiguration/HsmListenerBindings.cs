namespace HSMServer.ServerConfiguration
{
    // Immutable snapshot of the listener ports one instance of the server is actually
    // LISTENING on, captured while configuring Kestrel and injected into the /api/v1 area
    // guard and the authentication wiring. The same instance both drives options.Listen
    // and answers IsSitePort, so the guard can never disagree with the listeners; changes
    // take effect only when listeners and this registry are rebuilt on restart.
    public sealed record HsmListenerBindings(int SitePort, int SensorPort)
    {
        public bool IsSitePort(int port) => port == SitePort;
    }
}
