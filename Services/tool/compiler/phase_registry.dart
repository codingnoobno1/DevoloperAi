import '../core/ir.dart';

/// Represents a single discrete phase of the compilation/generation process.
/// Phases now operate on the ProjectIR (Intermediate Representation).
abstract class GeneratorPhase {
  /// Unique name of the phase.
  String get name;

  /// Version of the plugin/phase.
  String get version => "1.0.0";

  /// List of phase names that MUST run before this phase.
  List<String> get dependsOn => [];

  /// If true, failure in this phase stops the entire pipeline.
  bool get isCritical => true;

  /// Execute the phase logic on the IR.
  Future<void> run(ProjectIR ir);
}
