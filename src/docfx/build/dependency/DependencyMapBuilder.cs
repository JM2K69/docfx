// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Docs.Build
{
    /// <summary>
    /// A dependency map generated by one file building
    /// </summary>
    internal class DependencyMapBuilder
    {
        private readonly SourceMap _sourceMap;

        private readonly Scoped<ConcurrentHashSet<DependencyItem>> _dependencyItems = new();

        public DependencyMapBuilder(SourceMap sourceMap) => _sourceMap = sourceMap;

        public void AddDependencyItem(FilePath from, FilePath? to, DependencyType type, bool transitive = false)
        {
            if (to is null || from == to || from.Origin == FileOrigin.Fallback)
            {
                return;
            }

            var fromOriginalPath = _sourceMap.GetOriginalFilePath(from);
            var toOriginalPath = _sourceMap.GetOriginalFilePath(to);
            if (fromOriginalPath != null && toOriginalPath != null && fromOriginalPath == toOriginalPath)
            {
                return;
            }

            var item = new DependencyItem(
                fromOriginalPath is null ? from : fromOriginalPath,
                toOriginalPath is null ? to : toOriginalPath,
                type,
                transitive);

            Watcher.Write(() => _dependencyItems.Value.TryAdd(item));
        }

        public DependencyMap Build()
        {
            return new DependencyMap(Flatten());
        }

        private Dictionary<FilePath, HashSet<DependencyItem>> Flatten()
        {
            var result = new Dictionary<FilePath, HashSet<DependencyItem>>();
            var graph = _dependencyItems.Value
                .GroupBy(k => k.From)
                .ToDictionary(k => k.Key);

            foreach (var (from, value) in graph)
            {
                var dependencies = new HashSet<DependencyItem>();
                foreach (var item in value)
                {
                    var stack = new Stack<DependencyItem>();
                    stack.Push(item);
                    var visited = new HashSet<FilePath> { from };
                    while (stack.TryPop(out var current))
                    {
                        dependencies.Add(new DependencyItem(from, current.To, current.Type, current.Transitive));

                        // if the dependency destination is already in the result set, we can reuse it
                        if (current.To != from && current.Transitive && result.TryGetValue(current.To, out var nextDependencies))
                        {
                            foreach (var dependency in nextDependencies)
                            {
                                dependencies.Add(new DependencyItem(from, dependency.To, dependency.Type, dependency.Transitive));
                            }
                            continue;
                        }

                        if (graph.TryGetValue(current.To, out var toDependencies) && !visited.Contains(current.To) && current.Transitive)
                        {
                            foreach (var dependencyItem in toDependencies)
                            {
                                visited.Add(current.To);
                                stack.Push(dependencyItem);
                            }
                        }
                    }
                }
                result[from] = dependencies;
            }
            return result;
        }
    }
}