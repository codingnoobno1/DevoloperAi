import 'dart:io';
import 'package:analyzer/dart/analysis/utilities.dart';
import 'package:analyzer/dart/ast/ast.dart';
import 'package:analyzer/source/line_info.dart';
import '../reverse_config.dart';
import 'provenance.dart';

/// One parsed Dart source file: its syntactic AST + line info + relative path.
///
/// R1 uses a purely *syntactic* parse (`parseString`) — no resolution, no context
/// collection. Fast and dependency-free; sufficient for structure (classes,
/// widgets, routes). R3 can upgrade the connector passes to a resolved model.
class ParsedDart {
  final String relPath; // project-relative, forward slashes
  final String absPath;
  final CompilationUnit unit;
  final LineInfo lineInfo;

  ParsedDart(this.relPath, this.absPath, this.unit, this.lineInfo);

  /// Provenance for a node offset within this file.
  Provenance provenanceAt(int offset, {String confidence = 'syntactic'}) {
    final loc = lineInfo.getLocation(offset);
    return Provenance(relPath, loc.lineNumber, loc.columnNumber, confidence: confidence);
  }
}

/// Parses every discovered Dart file into a [ParsedDart]. Files that fail to
/// parse are skipped (reported via [errors]) rather than aborting the scan.
class DartSourceParser {
  final List<ParsedDart> units = [];
  final List<String> errors = [];

  DartSourceParser.parse(ScanContext ctx) {
    for (final abs in ctx.dartFiles) {
      try {
        final content = File(abs).readAsStringSync();
        final result = parseString(content: content, throwIfDiagnostics: false);
        units.add(ParsedDart(
          _rel(abs, ctx.options.projectRoot),
          abs,
          result.unit,
          result.lineInfo,
        ));
      } catch (e) {
        errors.add('parse failed: ${_rel(abs, ctx.options.projectRoot)} ($e)');
      }
    }
  }

  static String _rel(String abs, String root) =>
      abs.startsWith('$root/') ? abs.substring(root.length + 1) : abs;
}
