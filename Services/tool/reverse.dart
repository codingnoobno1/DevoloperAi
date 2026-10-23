import 'reverse/reverse_config.dart';
import 'reverse/reverse_engine.dart';
import 'reverse/passes/pass.dart';
import 'reverse/passes/screens_pass.dart';
import 'reverse/passes/models_pass.dart';
import 'reverse/passes/routes_pass.dart';
import 'reverse/passes/widgets_pass.dart';
import 'reverse/passes/api_pass.dart';
import 'reverse/passes/server_pass.dart';
import 'reverse/passes/services_pass.dart';
import 'reverse/passes/dto_pass.dart';
import 'reverse/passes/state_pass.dart';
import 'reverse/passes/theme_pass.dart';
import 'reverse/passes/datasource_pass.dart';
import 'reverse/ir/master_ir_assembler.dart';

/// Entry point for `thunder scan` — mirrors generate.dart / apply.dart / reset.dart.
Future<void> scanApp(List<String> args) async {
  final options = ScanOptions.fromArgs(args);
  final engine = ReverseEngine(options, _buildPasses(options));
  await engine.run();
}

/// The active pass list. Grows phase by phase (see reverseast_files.md §E).
/// R0: a manifest pass that proves the pipeline end-to-end.
List<ReversePass> _buildPasses(ScanOptions options) => [
      _ManifestPass(),
      // R1 — structure
      ScreensPass(),
      ModelsPass(),
      RoutesPass(),
      // R2 — widget lift
      WidgetsPass(),
      // R3 — connector (server_pass must follow api_pass; services after server)
      ApiPass(),
      ServerPass(),
      ServicesPass(),
      DtoPass(),
      // R4 — state / theme / data
      StatePass(),
      ThemePass(),
      DatasourcePass(),
      // R5 — consolidation (must run last)
      MasterIrAssembler(),
    ];

/// R0 skeleton pass: records the discovered source set as the provenance seed
/// (`sources.json`). Replaced/extended by the real passes in R1+.
class _ManifestPass implements ReversePass {
  @override
  String get id => 'manifest';

  @override
  String get outputFile => 'sources.json';

  @override
  PassResult run(ScanContext ctx) {
    final files = [
      for (final f in ctx.dartFiles)
        {'file': _rel(f, ctx.options.projectRoot), 'confidence': 'discovered'}
    ];
    return PassResult('manifest', outputFile, {'files': files},
        itemCount: files.length, notes: ['R0 skeleton: file discovery only']);
  }

  String _rel(String abs, String root) =>
      abs.startsWith('$root/') ? abs.substring(root.length + 1) : abs;
}
