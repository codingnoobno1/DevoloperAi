import 'dart:io';

/// Options controlling a single `thunder scan` run.
class ScanOptions {
  /// Root of the Flutter app to reverse-engineer.
  final String projectRoot;

  /// Directory that receives `.thunder/reverse/*.json` (defaults under projectRoot).
  final String outputDir;

  /// Emit only the connector bundle (api.json + server.json).
  final bool connectorOnly;

  /// Also emit forward-compatible config/custom_config/*.json (round-trip source).
  final bool emitConfig;

  /// Run the round-trip verifier after scanning.
  final bool verify;

  final bool debug;

  ScanOptions({
    required this.projectRoot,
    String? outputDir,
    this.connectorOnly = false,
    this.emitConfig = false,
    this.verify = false,
    this.debug = false,
  }) : outputDir = outputDir ?? '$projectRoot/.thunder/reverse';

  factory ScanOptions.fromArgs(List<String> args) {
    // First non-flag token after the command is the path (default: cwd).
    final positional = args.where((a) => !a.startsWith('-')).skip(1).toList();
    final root = positional.isNotEmpty ? positional.first : Directory.current.path;
    return ScanOptions(
      projectRoot: _normalize(root),
      connectorOnly: args.contains('--connector'),
      emitConfig: args.contains('--emit-config'),
      verify: args.contains('--verify'),
      debug: args.contains('--debug'),
    );
  }

  static String _normalize(String p) => p.replaceAll('\\', '/').replaceAll(RegExp(r'/+$'), '');
}

/// Shared state threaded through every pass during one scan.
///
/// R0 carries the discovered file list. R1+ attach the parsed Dart units and a
/// cross-pass `shared` bag (e.g. api-call sites the DTO pass consumes).
class ScanContext {
  final ScanOptions options;

  /// Absolute paths of every `.dart` file under the app's `lib/`.
  final List<String> dartFiles;

  /// Cross-pass scratch space keyed by producer (e.g. 'apiCalls', 'servers').
  final Map<String, dynamic> shared = {};

  ScanContext(this.options, this.dartFiles);
}
