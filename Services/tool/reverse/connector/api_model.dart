import '../parser/provenance.dart';

/// One resolved HTTP call site. JSON aligns with the C# connector's
/// `Services/Connector/Models/FrontendContract.cs` → `ApiCallSite`.
class ApiCallSite {
  final String method; // GET | POST | PUT | PATCH | DELETE | HEAD
  final String pathTemplate; // canonicalized: '/users/$id' → '/users/{id}'
  final String client; // dio | http
  final String? service; // enclosing class (e.g. OrderService)
  final String? serviceMethod; // enclosing method (e.g. fetchOrders)
  String? serverId; // FK into server.json (assigned by the server pass)
  final Provenance provenance;

  ApiCallSite({
    required this.method,
    required this.pathTemplate,
    required this.client,
    this.service,
    this.serviceMethod,
    this.serverId,
    required this.provenance,
  });

  Map<String, dynamic> toJson() => {
        'method': method,
        'endpoint': pathTemplate,
        'client': client,
        if (service != null) 'service': service,
        if (serviceMethod != null) 'serviceMethod': serviceMethod,
        if (serverId != null) 'server': serverId,
        'provenance': provenance.toJson(),
      };
}

/// An inferred backend target (base URL group).
class ServerInfo {
  final String id;
  final String? baseUrl; // null when dynamic
  final String source; // BaseOptions | const | env | cluster
  final String stackGuess; // rest | graphql | firebase | supabase | grpc
  final String confidence;
  int endpointsCount;
  final Provenance? provenance;

  ServerInfo({
    required this.id,
    required this.baseUrl,
    required this.source,
    this.stackGuess = 'rest',
    this.confidence = 'syntactic',
    this.endpointsCount = 0,
    this.provenance,
  });

  Map<String, dynamic> toJson() => {
        'id': id,
        'baseUrl': baseUrl,
        'source': source,
        'stackGuess': stackGuess,
        'confidence': confidence,
        'endpointsCount': endpointsCount,
        if (provenance != null) 'provenance': provenance!.toJson(),
      };
}
