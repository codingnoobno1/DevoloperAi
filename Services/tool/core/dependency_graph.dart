import 'models.dart';

class DependencyGraph {
  final Set<String> allFeatures = {};
  final Map<String, List<String>> featureScreens = {};
  final Map<String, String> screenToFeature = {};
  final Set<String> stateFeatures = {};
  final Set<String> repoFeatures = {};

  void resolve(AppModel model) {
    // 1. Identify Main Features (Root Screens)
    for (var screen in model.screens) {
      if (screen.type == 'main' || screen.type == 'tab') {
        allFeatures.add(screen.name);
        featureScreens[screen.name] = [screen.name];
        screenToFeature[screen.name] = screen.name;
      }
    }

    // 2. Resolve Sub-screens via Navigation Flow
    model.navigation.flow.forEach((sourceKey, flowData) {
      if (flowData is Map && flowData['target'] != null) {
        final targetScreen = flowData['target'] as String;
        
        // Find which feature this sourceKey belongs to
        // sourceKey is usually {feature}_{widget}_widget
        String? feature;
        for (var f in allFeatures) {
          if (sourceKey.startsWith(f)) {
            feature = f;
            break;
          }
        }

        if (feature != null) {
          featureScreens[feature]?.add(targetScreen);
          screenToFeature[targetScreen] = feature;
        } else {
          // If no parent feature found, it's its own feature
          allFeatures.add(targetScreen);
          featureScreens[targetScreen] = [targetScreen];
          screenToFeature[targetScreen] = targetScreen;
        }
      }
    });

    // 3. Catch-all for any screens not linked
    for (var screen in model.screens) {
      if (!screenToFeature.containsKey(screen.name)) {
        allFeatures.add(screen.name);
        featureScreens[screen.name] = [screen.name];
        screenToFeature[screen.name] = screen.name;
      }
    }

    // 4. Mark tab-children to avoid nested Scaffolds
    for (var screen in model.screens) {
      final layout = model.layouts[screen.layout];
      if (layout != null && layout.type == 'tabs') {
        for (var childId in layout.children) {
          final child = model.screens.firstWhere((s) => s.id == childId, orElse: () => screen);
          if (child.id == childId) {
            // Found child screen, mark it
            child.type = 'tab'; 
          }
        }
      }
    }

    // 5. State & Repo identification
    for (final f in allFeatures) {
      if (model.state.containsKey(f)) stateFeatures.add(f);
      if (model.repository.containsKey(f)) repoFeatures.add(f);
    }
  }

  void printDebug() {
    print('  📊 Total features identified: ${allFeatures.length}');
    for (var f in allFeatures) {
      print('    - Feature [$f]: Screens (${featureScreens[f]?.join(', ')})');
    }
  }
}
