import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class DashboardStatsChartsBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
SingleChildScrollView(
  padding: const EdgeInsets.all(16),
  child: Column(
    children: [
      Container(height: 200, decoration: BoxDecoration(color: Colors.blue.withOpacity(0.1), borderRadius: BorderRadius.circular(16)), child: const Center(child: Text("Chart Placeholder"))),
      const SizedBox(height: 20),
      GridView.count(shrinkWrap: true, physics: const NeverScrollableScrollPhysics(), crossAxisCount: 2, children: [${children.join(', ')}]),
    ],
  ),
)
""";
  }
}
