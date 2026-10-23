import 'dart:async';
import '../reverse_config.dart';

/// Output of a single reverse-extraction pass.
class PassResult {
  /// Pass id, e.g. "screens".
  final String name;

  /// Output path relative to the reverse root, e.g. "screens.json"
  /// or "connector/api.json".
  final String outputFile;

  /// JSON-serializable payload written to [outputFile].
  final dynamic json;

  /// Count of primary items extracted (screens, calls, models, ...).
  final int itemCount;

  /// Human-readable diagnostics for the scan report.
  final List<String> notes;

  /// Additional files this pass writes, keyed by path relative to the reverse
  /// root. Used when a pass finalizes another pass's output (e.g. server_pass
  /// re-emits connector/api.json with server IDs stamped on).
  final Map<String, dynamic> extraOutputs;

  const PassResult(
    this.name,
    this.outputFile,
    this.json, {
    this.itemCount = 0,
    this.notes = const [],
    this.extraOutputs = const {},
  });
}

/// A single reverse-engineering pass over the parsed project.
///
/// Passes are pure w.r.t. the filesystem: they read [ScanContext] and return a
/// [PassResult]; the engine owns all writing. Passes may publish/consume
/// cross-pass data via `ctx.shared` (e.g. api_pass publishes call sites the
/// dto_pass reads).
abstract class ReversePass {
  /// Stable id, also used in the scan report.
  String get id;

  /// Where this pass's JSON lands under the reverse root.
  String get outputFile;

  FutureOr<PassResult> run(ScanContext ctx);
}
