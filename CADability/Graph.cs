using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CADability
{
    internal class Graph
    {
        /// <summary>
        /// Returns all closed loops or cycles in the provided directed graph. The graph is defined by edges of type T. For each edge the <paramref name="adjacencyList"/>
        /// defines the following edges. In the resulting loops edges may be part of more than one loop.
        /// </summary>
        /// <typeparam name="T">type of the edges</typeparam>
        /// <param name="adjacencyList"></param>
        /// <returns>the closed cycles or loops</returns>
        public static List<List<T>> GetAllLoops<T>(Dictionary<T, List<T>> adjacencyList)
        {
            List<List<T>> res = new List<List<T>>();
            HashSet<T> available = new HashSet<T>(adjacencyList.Keys); // start with all edges, remove used edges
            Queue<List<T>> queue = new Queue<List<T>>(); // if a chain of edges has more possibilities to continue, all are examined.
            do
            {
                List<T> currentLoop = null;
                if (queue.Count > 0) currentLoop = queue.Dequeue(); // follow this chain
                else
                {
                    currentLoop = new List<T>();
                    if (available.Any())
                    {
                        T t = available.First();
                        available.Remove(t);
                        currentLoop.Add(t);
                    }
                    else break; // exit the loop: no more edges available and queue is empty
                }
                // now cuurentLoop contains at least one object and we try to follow this path
                while (currentLoop != null)
                {
                    if (adjacencyList.TryGetValue(currentLoop.Last(), out List<T> followedBy))
                    {
                        if (new HashSet<T>(followedBy).Overlaps(currentLoop))
                        {   // the loop is closed, we are done with this loop
                            // maybe the loop is closed somwhere in the middle, not at the beginning: we have to remove the first part of the current loop
                            // up to the point, where it was closed
                            while (!followedBy.Contains(currentLoop.First())) currentLoop.RemoveAt(0);
                            res.Add(currentLoop);
                            currentLoop = null;
                        }
                        else
                        {
                            if (followedBy.Count > 0)
                            {
                                followedBy.Reverse(); // DEBUG!
                                for (int i = 1; i < followedBy.Count; i++)
                                {   // maybe there are other branches, we push them into the queue for later processing
                                    List<T> otherBranch = new List<T>(currentLoop); // a clone of the current loop
                                    otherBranch.Add(followedBy[i]);
                                    available.Remove(followedBy[i]);
                                    queue.Enqueue(otherBranch); // we will follow this other branch later
                                }
                                currentLoop.Add(followedBy[0]);
                                available.Remove(followedBy[0]);
                            }
                            else
                            {
                                currentLoop = null; // no available edge is following, forget the accumulated chain
                            }
                        }
                    }
                    else
                    {   // there was no successor at the end of currentLoop. Dead end, forget the collected chain
                        currentLoop = null;
                    }
                }
            } while (available.Any() || queue.Any());
            return res;
        }
    }
}
