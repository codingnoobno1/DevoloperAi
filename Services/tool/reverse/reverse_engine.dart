import 'dart:io';
import '../core/file_writer.dart'; // reuse: FileWriter.clean (dir reset)
import '../utils/logger.dart'; // reuse: Logger
import 'reverse_config.dart';
import 'parser/dart_source_parser.dart';
import 'passes/pass.dart';
import 'output/json_writer.dart';
import 'output/scan_report.dart';

/// Orchestrates one `thunder scan`: discover → run passes → write JSON → report.
class ReverseEngine {
  final ScanOptions options;
  final List<ReversePass> passes;

  ReverseEngine(this.options, this.passes);

  Future<void> run() async {
    Logger.debugEnabled = options.debug;
    Logger.info('Reverse AST scan → ${options.projectRoot}');

    final libDir = Directory('${options.projectRoot}/lib');
    if (!libDir.existsSync()) {
      Logger.error('No lib/ directory at ${options.projectRoot} — not a Flutter app root?');
      exit(1);
    }

    final sw = Stopwatch()..start();

    // 1. Discover Dart sources.
    final dartFiles = libDir
        .listSync(recursive: true)
        .whereType<File>()
        .map((f) => f.path.replaceAll('\\', '/'))
        .where((p) => p.endsWith('.dart') && !p.endsWith('.g.dart') && !p.endsWith('.freezed.dart'))
        .toList()
      ..sort();

    Logger.info('Discovered ${dartFiles.length} Dart files under lib/');

    // 2. Build shared context and parse every source once (shared across passes).
    final ctx = ScanContext(options, dartFiles);
    final parser = DartSourceParser.parse(ctx);
    ctx.shared['units'] = parser.units;
    if (parser.errors.isNotEmpty) {
      Logger.warn('${parser.errors.length} file(s) failed to parse');
      for (final e in parser.errors) Logger.debug(e);
    }
    Logger.info('Parsed ${parser.units.length} units');

    // 3. Reset output root and run passes.
    FileWriter.clean(options.outputDir);
    final writer = JsonWriter(options.outputDir);
    final report = ScanReport(options.projectRoot)..filesScanned = dartFiles.length;

    for (final pass in passes) {
      try {
        final result = await pass.run(ctx);
        writer.write(result.outputFile, result.json);
        result.extraOutputs.forEach(writer.write);
        // Accumulate each pass's payload so a downstream assembler (master_ir) can consolidate.
        final outs = (ctx.shared['out'] ??= <String, dynamic>{}) as Map<String, dynamic>;
        outs[result.name] = result.json;
        result.extraOutputs.forEach((path, json) => outs[path] = json);
        report.add(result);
        Logger.debug('pass ${pass.id} → ${result.itemCount} items');
      } catch (e, st) {
        Logger.error('pass ${pass.id} failed', e);
        if (options.debug) print(st);
      }
    }

    // 4. Report.
    sw.stop();
    report.elapsedMs = sw.elapsedMilliseconds;
    writer.write('scan_report.json', report.toJson());
    report.printSummary();
    Logger.success('Wrote ${options.outputDir}/');
  }
}
