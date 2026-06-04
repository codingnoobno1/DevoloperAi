import 'ir.dart';
import 'ast.dart';
import '../utils/logger.dart';

/// IROptimizer v2.0: Type-safe AST transformations.
/// Performs structural optimizations on the ProjectIR object graph.
class IROptimizer {
  
  static void optimize(ProjectIR ir) {
    Logger.info("🚀 Running IR Optimization Passes...");
    
    for (var feature in ir.features.values) {
      for (var i = 0; i < feature.screens.length; i++) {
        feature.screens[i].root = _optimizeNode(feature.screens[i].root, ir.optimization);
      }
      
      ir.features[feature.name]?.widgetASTs.updateAll((key, node) {
        return _optimizeNode(node, ir.optimization);
      });
    }
    
    Logger.success("Optimization Complete: ${ir.optimization.redundantWidgetsRemoved} widgets removed, ${ir.optimization.layoutNodesFlattened} nodes flattened.");
  }

  static WidgetNode _optimizeNode(WidgetNode node, OptimizationStats stats) {
    // 1. Recursive optimization (Bottom-up)
    WidgetNode optimized = node;

    if (node is ColumnNode) {
      optimized = ColumnNode(
        children: node.children.map((c) => _optimizeNode(c, stats)).toList(),
        crossAxisAlignment: node.crossAxisAlignment,
        mainAxisAlignment: node.mainAxisAlignment,
        mainAxisSize: node.mainAxisSize,
        spacing: node.spacing,
      );
    } else if (node is RowNode) {
      optimized = RowNode(
        children: node.children.map((c) => _optimizeNode(c, stats)).toList(),
        crossAxisAlignment: node.crossAxisAlignment,
        mainAxisAlignment: node.mainAxisAlignment,
        spacing: node.spacing,
      );
    } else if (node is ContainerNode) {
      if (node.child != null) {
        final optChild = _optimizeNode(node.child!, stats);
        
        // Redundant Container Flattening
        if (_isTransparentContainer(node)) {
          stats.redundantWidgetsRemoved++;
          stats.log("Removed redundant Container around ${optChild.runtimeType}");
          return optChild;
        }

        optimized = ContainerNode(
          child: optChild,
          padding: node.padding,
          margin: node.margin,
          decoration: node.decoration,
          width: node.width,
          height: node.height,
          alignment: node.alignment,
        );
      }
    } else if (node is PaddingNode) {
      optimized = PaddingNode(
        child: _optimizeNode(node.child, stats),
        padding: node.padding,
      );
    } else if (node is CenterNode) {
      optimized = CenterNode(child: _optimizeNode(node.child, stats));
    } else if (node is ExpandedNode) {
      optimized = ExpandedNode(child: _optimizeNode(node.child, stats), flex: node.flex);
    }

    // 2. Multi-child Layout Merging (Column in Column)
    if (optimized is ColumnNode) {
      final flattenedChildren = <WidgetNode>[];
      bool changed = false;
      
      for (var child in optimized.children) {
        if (child is ColumnNode && child.spacing == null && optimized.spacing == null) {
          flattenedChildren.addAll(child.children);
          stats.layoutNodesFlattened++;
          stats.log("Flattened nested Column into parent Column");
          changed = true;
        } else {
          flattenedChildren.add(child);
        }
      }
      
      if (changed) {
        return ColumnNode(
          children: flattenedChildren,
          crossAxisAlignment: optimized.crossAxisAlignment,
          mainAxisAlignment: optimized.mainAxisAlignment,
          mainAxisSize: optimized.mainAxisSize,
          spacing: optimized.spacing,
        );
      }
    }

    return optimized;
  }

  static bool _isTransparentContainer(ContainerNode node) {
    return node.padding == null && 
           node.margin == null && 
           node.decoration == null && 
           node.width == null && 
           node.height == null && 
           node.alignment == null;
  }
}
