import 'dart:convert';
import '../core/file_writer.dart';
import '../core/ir.dart';

class SummaryGenerator {
  /// New IR-Native generation method.
  Future<void> generateFromIR(ProjectIR ir) async {
    final Map<String, dynamic> summary = {};
    
    // 1. Entry point & Meta
    summary['version'] = ir.metadata['version'] ?? 'unknown';
    summary['ui_style'] = ir.theme.isValid ? 'material' : 'legacy';

    // 2. Features IR analysis
    final Map<String, dynamic> features = {};
    ir.features.forEach((name, fIR) {
      features[name] = {
        'screens': fIR.screens.map((s) => s.name).toList(),
        'widget_count': fIR.widgetASTs.length,
      };
    });
    summary['features'] = features;

    // 3. Service analysis
    summary['services'] = ir.services.map((k, v) => MapEntry(k, v.type));

    // 4. Global Stats
    summary['stats'] = {
      'feature_count': ir.features.length,
      'service_count': ir.services.length,
      'total_screens': ir.features.values.fold<int>(0, (p, f) => p + f.screens.length),
    };

    final code = JsonEncoder.withIndent('  ').convert(summary);
    FileWriter.write('lib_gen/app.json', code);
  }
}
