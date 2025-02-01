﻿using GraphSharp;
using PalCalc.Model;
using PalCalc.Solver.PalReference;
using PalCalc.Solver.Tree;
using PalCalc.UI.Model;
using QuickGraph;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PalCalc.UI.ViewModel.GraphSharp
{
    public class BreedingEdge : TypedEdge<BreedingTreeNodeViewModel>
    {
        public BreedingEdge(BreedingTreeNodeViewModel parent, BreedingTreeNodeViewModel child) : base(parent, child, EdgeTypes.Hierarchical) { }

        public override string ToString() => "";
    }

    public class BreedingGraph : HierarchicalGraph<BreedingTreeNodeViewModel, BreedingEdge>
    {
        private BreedingGraph(CachedSaveGame source, GameSettings settings, BreedingTree tree)
        {
            Tree = tree;
            Nodes = tree.AllNodes.Select(p => new BreedingTreeNodeViewModel(source, settings, p.Item1)).ToList();
        }

        public BreedingTree Tree { get; private set; }
        public List<BreedingTreeNodeViewModel> Nodes { get; }

        public bool NeedsRefresh => Nodes.Any(n => n.NeedsRefresh);

        public BreedingTreeNodeViewModel NodeFor(IBreedingTreeNode pref) => Nodes.Single(n => n.Value == pref);

        public static BreedingGraph FromPalReference(CachedSaveGame source, GameSettings settings, IPalReference palRef)
        {
            var tree = new BreedingTree(palRef);
            var result = new BreedingGraph(source, settings, tree);

            result.AddVertexRange(result.Nodes);

            // breeding tree is upside down relative to breeding direction
            foreach (var (child, _) in tree.AllNodes)
                foreach (var parent in child.Children)
                    result.AddEdge(new BreedingEdge(parent: result.NodeFor(parent), child: result.NodeFor(child)));

            return result;
        }
    }
}
