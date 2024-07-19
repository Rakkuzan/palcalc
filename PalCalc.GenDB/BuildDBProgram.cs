﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PalCalc.Model;
using System.Security.Cryptography;

namespace PalCalc.GenDB
{
    internal class BuildDBProgram
    {
        // min. number of times you need to breed Key1 to get a Key2 (to prune out path checks between pals which would exceed the max breeding steps)
        static Dictionary<Pal, Dictionary<Pal, int>> CalcMinDistances(PalDB db)
        {
            Logging.InitCommonFull();

            Dictionary<Pal, Dictionary<Pal, int>> palDistances = new Dictionary<Pal, Dictionary<Pal, int>>();

            foreach (var p in db.Pals)
                palDistances.Add(p, new Dictionary<Pal, int>() { { p, 0 } });

            List<(Pal, Pal)> toCheck = new List<(Pal, Pal)>(db.Pals.SelectMany(p => db.Pals.Where(i => i != p).Select(p2 => (p, p2))));
            bool didUpdate = true;

            while (didUpdate)
            {
                didUpdate = false;

                List<(Pal, Pal)> resolved = new List<(Pal, Pal)>();
                List<(Pal, Pal)> unresolved = new List<(Pal, Pal)>();
                foreach (var next in toCheck)
                {
                    var src = next.Item1;
                    var target = next.Item2;

                    // check if there's a direct way to breed from src to target
                    if (db.BreedingByChild[target].Any(kvp => kvp.Key.Pal == src))
                    {
                        if (!palDistances[src].ContainsKey(target) || palDistances[src][target] != 1)
                        {
                            didUpdate = true;
                            palDistances[src][target] = 1;
                            resolved.Add(next);
                        }
                        continue;
                    }

                    // check if there's a possible child of this `src` with known distance to target
                    var childWithShortestDistance = db.BreedingByParent[src].Values
                        .SelectMany(l => l.Select(b => b.Child))
                        .Where(child => palDistances[child].ContainsKey(target))
                        .OrderBy(child => palDistances[child][target])
                        .FirstOrDefault();

                    if (childWithShortestDistance != null)
                    {
                        if (!palDistances[src].ContainsKey(target) || palDistances[src][target] != palDistances[childWithShortestDistance][target] + 1)
                        {
                            didUpdate = true;
                            palDistances[src][target] = palDistances[childWithShortestDistance][target] + 1;
                            resolved.Add(next);
                        }
                        continue;
                    }

                    unresolved.Add(next);
                }

                Console.WriteLine("Resolved {0} entries with {1} left unresolved", resolved.Count, unresolved.Count);

                if (!didUpdate)
                {
                    // the remaining (src,target) pairs are impossible
                    foreach (var p in unresolved)
                    {
                        palDistances[p.Item1].Add(p.Item2, 10000);
                    }
                }
            }

            return palDistances;
        }

        static void Main(string[] args)
        {
            var pals = new List<Pal>();
            pals.AddRange(ParseScrapedJson.ReadPals());

            var traits = new List<Trait>();
            traits.AddRange(ParseScrapedJson.ReadTraits());

            var localizations = ParseLocalizedNameJson.ParseLocalizedNames();

            foreach (var kvp in localizations)
            {
                var lang = kvp.Key;
                var i10n = kvp.Value;

                var missingPals = pals.Where(p => !i10n.PalsByLowerInternalName.ContainsKey(p.InternalName.ToLower())).ToList();
                var missingTraits = traits.Where(t => !i10n.TraitsByLowerInternalName.ContainsKey(t.InternalName.ToLower())).ToList();

                if (missingPals.Count > 0 || missingTraits.Count > 0)
                {
                    Console.WriteLine("{0} missing entries:", lang);

                    if (missingPals.Count > 0)
                    {
                        Console.WriteLine("Pals");
                        foreach (var p in missingPals) Console.WriteLine("- {0}", p.InternalName);
                    }

                    if (missingTraits.Count > 0)
                    {
                        Console.WriteLine("Traits");
                        foreach (var t in missingTraits) Console.WriteLine("- {0}", t.InternalName);
                    }
                }
            }

            foreach (var pal in pals)
                pal.LocalizedNames = localizations
                    .Select(kvp => (kvp.Key, kvp.Value.PalsByLowerInternalName.GetValueOrDefault(pal.InternalName.ToLower())))
                    .Where(p => p.Item2 != null)
                    .ToDictionary(p => p.Key, p => p.Item2);

            foreach (var trait in traits)
                trait.LocalizedNames = localizations
                    .Select(kvp => (kvp.Key, kvp.Value.TraitsByLowerInternalName.GetValueOrDefault(trait.InternalName.ToLower())))
                    .Where(p => p.Item2 != null)
                    .ToDictionary(p => p.Key, p => p.Item2);

            var specialCombos = ParseScrapedJson.ReadExclusiveBreedings();

            foreach (var (p1, p2, c) in specialCombos)
            {
                if (!pals.Any(p => p.InternalName == p1.Item1) || !pals.Any(p => p.InternalName == p2.Item1) || !pals.Any(p => p.InternalName == c))
                    throw new Exception("Unrecognized pal name");
            }

            foreach (var pal in pals)
                if (pal.GuaranteedTraitInternalIds.Any(id => !traits.Any(t => t.InternalName == id)))
                    throw new Exception("Unrecognized trait ID");

            Pal Child(GenderedPal parent1, GenderedPal parent2)
            {
                if (parent1.Pal == parent2.Pal) return parent1.Pal;

                var specialCombo = specialCombos.Where(c =>
                    (parent1.Pal.InternalName == c.Item1.Item1 && parent2.Pal.InternalName == c.Item2.Item1) ||
                    (parent2.Pal.InternalName == c.Item1.Item1 && parent1.Pal.InternalName == c.Item2.Item1)
                );

                if (specialCombo.Any())
                {
                    return pals.Single(p =>
                        p.InternalName == specialCombo.Single(c =>
                        {
                            bool Matches(GenderedPal parent, string pal, PalGender? gender) =>
                                parent.Pal.InternalName == pal && (gender == null || parent.Gender == gender);

                            var ((p1, p1g), (p2, p2g), child) = c;

                            return (
                                (Matches(parent1, p1, p1g) && Matches(parent2, p2, p2g)) ||
                                (Matches(parent2, p1, p1g) && Matches(parent1, p2, p2g))
                            );
                        }).Item3
                    );
                }

                int childPower = (int)Math.Floor((parent1.Pal.BreedingPower + parent2.Pal.BreedingPower + 1) / 2.0f);
                return pals
                    .Where(p => !specialCombos.Any(c => p.InternalName == c.Item3)) // pals produced by a special combo can _only_ be produced by that combo
                    .OrderBy(p => Math.Abs(p.BreedingPower - childPower))
                    .ThenBy(p => p.InternalIndex)
                    // if there are two pals with the same internal index, prefer the non-variant pal
                    .ThenBy(p => p.Id.IsVariant ? 1 : 0)
                    .First();
            }

            var db = PalDB.MakeEmptyUnsafe("v13");
            db.Breeding = pals
                .SelectMany(parent1 => pals.Select(parent2 => (parent1, parent2)))
                .Select(pair => pair.parent1.GetHashCode() > pair.parent2.GetHashCode() ? (pair.parent1, pair.parent2) : (pair.parent2, pair.parent1))
                .Distinct()
                .SelectMany(pair => new[] {
                    (
                        new GenderedPal() { Pal = pair.Item1, Gender = PalGender.FEMALE },
                        new GenderedPal() { Pal = pair.Item2, Gender = PalGender.MALE }
                    ),
                    (
                        new GenderedPal() { Pal = pair.Item1, Gender = PalGender.MALE },
                        new GenderedPal() { Pal = pair.Item2, Gender = PalGender.FEMALE }
                    )
                })
                // get the results of breeding with swapped genders (for results where the child is determined by parent genders)
                .Select(p => new BreedingResult
                {
                    Parent1 = p.Item1,
                    Parent2 = p.Item2,
                    Child = Child(p.Item1, p.Item2)
                })
                // simplify cases where the child is the same regardless of gender
                .GroupBy(br => br.Child)
                .SelectMany(cg =>
                    cg
                        .GroupBy(br => (br.Parent1.Pal, br.Parent2.Pal))
                        .SelectMany(g =>
                        {
                            var results = g.ToList();
                            if (results.Count == 1) return results;

                            return
                            [
                                new BreedingResult()
                                {
                                    Parent1 = new GenderedPal()
                                    {
                                        Pal = results.First().Parent1.Pal,
                                        Gender = PalGender.WILDCARD
                                    },
                                    Parent2 = new GenderedPal()
                                    {
                                        Pal = results.First().Parent2.Pal,
                                        Gender = PalGender.WILDCARD
                                    },
                                    Child = results.First().Child
                                }
                            ];
                        })
                )
                .ToList();

            db.PalsById = pals.ToDictionary(p => p.Id);

            db.Traits = traits;

            var genderProbabilities = ParseScrapedJson.ReadGenderProbabilities();
            db.BreedingGenderProbability = pals.ToDictionary(
                p => p,
                p => genderProbabilities[p.InternalName]
            );

            db.MinBreedingSteps = CalcMinDistances(db);

            File.WriteAllText("../PalCalc.Model/db.json", db.ToJson());
        }
    }
}