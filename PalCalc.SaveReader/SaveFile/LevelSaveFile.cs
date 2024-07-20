﻿using PalCalc.Model;
using PalCalc.SaveReader.SaveFile.Support.Level;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PalCalc.SaveReader.SaveFile
{
    public class LevelSaveData
    {
        public List<PalInstance> Pals { get; set; }
        public List<PlayerInstance> Players { get; set; }
        public List<GuildInstance> Guilds { get; set; }
    }

    public class RawLevelSaveData
    {
        public List<GvasCharacterInstance> Characters { get; set; }
        public List<PalContainer> Containers { get; set; }
        public List<GuildInstance> Groups { get; set; }
    }

    public class LevelSaveFile : ISaveFile
    {
        private static ILogger logger = Log.ForContext<LevelSaveFile>();

        public LevelSaveFile(string filePath) : base(filePath) { }

        private Guid MostCommonOwner(PalContainer container, Dictionary<Guid, Guid> palOwnersByInstanceId) => container.Slots.GroupBy(s => palOwnersByInstanceId.GetValueOrElse(s.InstanceId, Guid.Empty)).MaxBy(g => g.Count()).Key;

        public RawLevelSaveData ReadRawCharacterData()
        {
            var containerVisitor = new PalContainerVisitor();
            var instanceVisitor = new CharacterInstanceVisitor();
            var groupVisitor = new GroupVisitor();
            VisitGvas(containerVisitor, instanceVisitor, groupVisitor);

            return new RawLevelSaveData()
            {
                Characters = instanceVisitor.Result,
                Containers = containerVisitor.CollectedContainers,
                Groups = groupVisitor.Result
            };
        }

        private LevelSaveData BuildResult(PalDB db, List<GvasCharacterInstance> characters, List<GuildInstance> guilds, List<PalContainer> containers, Dictionary<string, LocationType> containerTypeById)
        {
            var result = new LevelSaveData()
            {
                Pals = new List<PalInstance>(),
                Players = new List<PlayerInstance>(),
                Guilds = guilds,
            };

            foreach (var gvasInstance in characters)
            {
                if (gvasInstance.IsPlayer)
                {
                    result.Players.Add(new PlayerInstance()
                    {
                        PlayerId = gvasInstance.PlayerId.ToString(),
                        InstanceId = gvasInstance.InstanceId.ToString(),
                        Name = gvasInstance.NickName,
                        Level = gvasInstance.Level,
                    });
                }
                else
                {
                    var sanitizedCharId = gvasInstance.CharacterId.Replace("Boss_", "", StringComparison.InvariantCultureIgnoreCase);
                    var pal = db.Pals.FirstOrDefault(p => p.InternalName.ToLower() == sanitizedCharId.ToLower());

                    if (pal == null)
                    {
                        // skip unrecognized pals
                        logger.Warning("unrecognized pal '{name}', skipping", sanitizedCharId);
                        continue;
                    }

                    if (!containerTypeById.ContainsKey(gvasInstance.ContainerId?.ToString()))
                    {
                        // Level.sav contains a list of all known containers, but there are some cases where a pal
                        // references a container ID not in this list. the cause is not known, but I've seen "effective"
                        // container sizes from 1 to 40. there's no clear answer to "where" this container is (or its
                        // pals), so we won't bother referencing it
                        //
                        // (might be due to butchered pals? https://github.com/tylercamp/palcalc/issues/12#issuecomment-2101688781)
                        logger.Warning("unrecognized pal container id '{id}', skipping", gvasInstance.ContainerId);
                        continue;
                    }

                    var container = containers.Single(c => c.Id == gvasInstance.ContainerId.ToString());
                    if (!container.Slots.Any(s => s.InstanceId == gvasInstance.InstanceId))
                    {
                        logger.Debug("pal instance data '{palId}' references container '{containerId}' but the container has no record of this pal, skipping", gvasInstance.InstanceId, container.Id);
                        continue;
                    }

                    var passives = gvasInstance.PassiveSkills
                        .Select(name =>
                        {
                            var passive = db.PassiveSkills.FirstOrDefault(t => t.InternalName == name);
                            if (passive == null)
                            {
                                logger.Warning("unrecognized passive skill '{internalName}' on pal {Pal}, skipping", name, gvasInstance.CharacterId);
                            }
                            return passive ?? new UnrecognizedPassiveSkill(name);
                        })
                        .ToList();

                    result.Pals.Add(new PalInstance()
                    {
                        Pal = pal,
                        InstanceId = gvasInstance.InstanceId.ToString(),
                        OwnerPlayerId = gvasInstance.OwnerPlayerId?.ToString() ?? gvasInstance.OldOwnerPlayerIds.First().ToString(),

                        IV_HP = gvasInstance.TalentHp ?? 0,
                        IV_Melee = gvasInstance.TalentMelee ?? 0,
                        IV_Shot = gvasInstance.TalentShot ?? 0,
                        IV_Defense = gvasInstance.TalentDefense ?? 0,

                        Level = gvasInstance.Level,
                        NickName = gvasInstance.NickName,
                        Gender = gvasInstance.Gender.Contains("Female") ? PalGender.FEMALE : PalGender.MALE,
                        PassiveSkills = passives,
                        Location = new PalLocation()
                        {
                            ContainerId = gvasInstance.ContainerId.ToString(),
                            Type = containerTypeById[gvasInstance.ContainerId.ToString()],
                            Index = gvasInstance.SlotIndex,
                        }
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Uses the provided list of players, which specify party and palbox container IDs, to read the list of character instances and
        /// properly associate them with their owner + container type.
        /// </summary>
        public LevelSaveData ReadCharacterData(PalDB db, List<PlayersSaveFile> playersFiles)
        {
            if (playersFiles.Count == 0) return ReadCharacterData(db);

            logger.Debug("parsing content with players list");

            var parsed = ReadRawCharacterData();

            var players = playersFiles.Select(pf => pf.ReadPlayerContent()).ToList();

            var containerTypeById = parsed.Containers.ToDictionary(c => c.Id, c =>
            {
                if (players.Any(p => p.PartyContainerId == c.Id)) return LocationType.PlayerParty;
                if (players.Any(p => p.PalboxContainerId == c.Id)) return LocationType.Palbox;

                return LocationType.Base;
            });

            var result = BuildResult(db, parsed.Characters, parsed.Groups, parsed.Containers, containerTypeById);
            logger.Debug("done");
            return result;
        }

        /// <summary>
        /// Reads the list of character instances and attempts to infer the pal container types based on the owning player and container size.
        /// </summary>
        private LevelSaveData ReadCharacterData(PalDB db)
        {
            logger.Debug("parsing content");

            var parsed = ReadRawCharacterData();

            logger.Debug("processing data");

            var ownersByInstanceId = parsed.Characters.Where(c => c.OwnerPlayerId != null).ToDictionary(c => c.InstanceId, c => c.OwnerPlayerId.Value);

            var containersById = parsed.Containers.ToDictionary(c => c.Id);
            var containerOwners = parsed.Containers.ToDictionary(c => c.Id, c => MostCommonOwner(c, ownersByInstanceId));
            var palBoxesByPlayerId = parsed.Containers.Where(c => c.Slots.Count > GameConstants.PlayerPartySize).GroupBy(c => MostCommonOwner(c, ownersByInstanceId)).ToDictionary(g => g.Key, g => g.MaxBy(c => c.MaxEntries));

            var containerTypeById = parsed.Containers.ToDictionary(c => c.Id, c =>
            {
                if (c.MaxEntries == GameConstants.PlayerPartySize) return LocationType.PlayerParty;

                var palBoxForPlayer = palBoxesByPlayerId[containerOwners[c.Id]];
                if (c.Id == palBoxForPlayer.Id) return LocationType.Palbox;

                return LocationType.Base;
            });

            var result = BuildResult(db, parsed.Characters, parsed.Groups, parsed.Containers, containerTypeById);

            logger.Debug("done");
            return result;
        }
    }
}
