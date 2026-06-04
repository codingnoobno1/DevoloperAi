import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class DashboardTabbedBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
DefaultTabController(
  length: ${children.length},
  child: Column(
    children: [
      const TabBar(tabs: [Tab(icon: Icon(Icons.home)), Tab(icon: Icon(Icons.settings))]),
      Expanded(child: TabBarView(children: [${children.join(', ')}])),
    ],
  ),
)
""";
  }
}
