namespace PipeMux.Shared;

public enum BrokerTransportKind {
    NamedPipe,
    UnixSocket
}

public readonly record struct BrokerEndpoint(BrokerTransportKind Transport, string Value);
