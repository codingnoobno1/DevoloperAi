import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class DashboardDrawerBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
Scaffold(
  appBar: AppBar(title: const Text("Dashboard")),
  drawer: Drawer(child: ListView(children: [const DrawerHeader(child: Text("Menu")), ListTile(title: const Text("Home")), ListTile(title: const Text("Settings"))])),
  body: Column(children: [${children.join(', ')}]),
)
""";
  }
}
