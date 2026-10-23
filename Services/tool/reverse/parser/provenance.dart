/// Where an extracted fact came from — attached to every reverse-scan record so
/// each claim is auditable back to a source line.
///
/// confidence: `resolved` (element model) | `syntactic` (AST only) | `heuristic` (regex fallback).
class Provenance {
  final String file; // project-relative
  final int line;
  final int column;
  final String confidence;

  const Provenance(this.file, this.line, this.column, {this.confidence = 'syntactic'});

  Map<String, dynamic> toJson() => {
        'file': file,
        'line': line,
        'column': column,
        'confidence': confidence,
      };
}
