import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../utils/string_utils.dart';

class TabViewLayoutBuilder implements LayoutBuilder {
  @override
  WidgetNode build(LayoutModel config, List<WidgetNode> children, AppModel model) {
    final List<String> tabList = config.tabs.isNotEmpty ? config.tabs : config.children;
    
    final tabViews = tabList.map((t) => "const ${StringUtils.toPascalCase(t)}Screen()").join(", ");
    
    final navItems = config.slots.map((s) {
      final label = s['label'] ?? StringUtils.capitalize(s['id'] ?? 'Tab');
      final iconName = s['icon'] ?? 'home';
      final icon = _mapIcon(iconName);
      return """NavItem(
      label: '$label',
      icon: $icon,
    )""";
    }).join(", ");

    final length = tabList.length > 0 ? tabList.length : config.slots.length;

    return RawNode("""
DefaultTabController(
  length: $length,
  child: Builder(
    builder: (context) {
      final tabController = DefaultTabController.of(context);
      return Scaffold(
        body: TabBarView(
          physics: const NeverScrollableScrollPhysics(),
          children: [$tabViews],
        ),
        bottomNavigationBar: ListenableBuilder(
          listenable: tabController,
          builder: (context, _) {
            return Container(
              decoration: BoxDecoration(
                boxShadow: [
                  BoxShadow(
                    color: Colors.black.withOpacity(0.05),
                    blurRadius: 20,
                    offset: const Offset(0, -5),
                  ),
                ],
              ),
              child: UIFactory.navBar(
                currentIndex: tabController.index,
                items: [$navItems],
                onTap: (index) => tabController.animateTo(index),
              ),
            );
          },
        ),
      );
    }
  ),
)
""");
  }

  String _mapIcon(String name) {
    switch (name.toLowerCase()) {
      case 'home': return 'Icons.home_rounded';
      case 'route': return 'Icons.map_rounded';
      case 'person': return 'Icons.person_rounded';
      case 'history': return 'Icons.history_rounded';
      case 'settings': return 'Icons.settings_rounded';
      default: return 'Icons.circle_rounded';
    }
  }
}
