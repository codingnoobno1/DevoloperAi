import '../reverse_config.dart';
import '../passes/pass.dart';

/// Consolidates every pass output into a single `master_ir.json` — the reverse
/// mirror of Thunder's forward Master IR (master_architecture.md):
/// `orchestration` (quick layer) + `implementation` (depth layer).
///
/// Runs LAST; reads the accumulated `ctx.shared['out']` map keyed by pass name
/// / output path (populated by the engine after each pass).
class MasterIrAssembler implements ReversePass {
  @override
  String get id => 'master_ir';
  @override
  String get outputFile => 'master_ir.json';

  @override
  PassResult run(ScanContext ctx) {
    final out = (ctx.shared['out'] as Map<String, dynamic>?) ?? const {};

    List<dynamic> list(String name, String key) {
      final payload = out[name];
      if (payload is Map && payload[key] is List) return payload[key] as List;
      return const [];
    }

    final screens = list('screens', 'screens');
    final widgets = list('widgets', 'widgets');
    final models = list('models', 'models');
    final endpoints = list('api', 'calls');
    final servers = list('server', 'servers');
    final services = list('services', 'services');
    final layoutNodes = (out['layout.json'] is Map && out['layout.json']['nodes'] is List)
        ? out['layout.json']['nodes'] as List
        : const [];

    final manifest = {
      'version': '1.0.0-reverse',
      'orchestration': {
        'screens': screens,
        'widgets': widgets,
        'navigation': {
          'initialRoute': (out['routes'] is Map) ? out['routes']['initialRoute'] : null,
          'routes': list('routes', 'routes'),
        },
        'models': models,
      },
      'implementation': {
        'services': services,
        'endpoints': endpoints,
        'servers': servers,
        'dtos': list('dto', 'dtos'),
        'state_management': list('state', 'blocs'),
        'data_sources': list('datasource', 'sources'),
        'theme_tokens': list('theme', 'themes'),
        'layout_depth': layoutNodes,
      },
      'stats': {
        'screens': screens.length,
        'models': models.length,
        'endpoints': endpoints.length,
        'servers': servers.length,
        'services': services.length,
      },
    };

    return PassResult('master_ir', outputFile, manifest,
        itemCount: 1, notes: ['consolidated orchestration + implementation']);
  }
}
