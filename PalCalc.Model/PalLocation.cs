﻿using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PalCalc.Model
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LocationType
    {
        PlayerParty,
        Palbox,
        Base,
        ViewingCage,
        Custom,
    }

    public class PalLocation
    {
        public string ContainerId { get; set; }
        public LocationType Type { get; set; }
        public int Index { get; set; }

        // pal box is 6x5
        public override string ToString()
        {
            string indexStr;
            if (Type == LocationType.Palbox)
            {
                var coord = PalboxCoord.FromSlotIndex(Index);
                indexStr = coord.ToString();
            }
            else
            {
                indexStr = $"Slot #{Index+1}";
            }

            return $"{Type} ({indexStr})";
        }

        public override int GetHashCode() => HashCode.Combine(Type, Index);
    }

    public class PalboxCoord
    {
        // all 1-indexed
        public int Tab { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }

        public int X => Column;
        public int Y => Row;

        public override string ToString() => $"Tab {Tab} at ({X},{Y})";

        public static PalboxCoord FromSlotIndex(int slotIndex)
        {
            var numCols = GameConstants.PalBox_GridWidth;
            var numRows = GameConstants.PalBox_GridHeight;
            var palsPerTab = numCols * numRows;

            var tab = (slotIndex - slotIndex % palsPerTab) / palsPerTab;
            slotIndex -= tab * palsPerTab;

            var row = (slotIndex - slotIndex % numCols) / numCols;
            slotIndex -= row * numCols;
            var col = slotIndex;

            return new PalboxCoord() { Tab = tab + 1, Row = row + 1, Column = col + 1 };
        }
    }

    public class BaseCoord
    {
        // all 1-indexed
        public int Row { get; set; }
        public int Column { get; set; }

        public int X => Column;
        public int Y => Row;

        public override string ToString() => $"Slot ({X},{Y})";

        public static BaseCoord FromSlotIndex(int slotIndex)
        {
            var row = (slotIndex - slotIndex % GameConstants.Base_GridWidth) / GameConstants.Base_GridWidth;
            slotIndex -= row * GameConstants.Base_GridWidth;

            return new BaseCoord() { Row = row + 1, Column = slotIndex + 1 };
        }
    }

    // TODO viewing cage coord
}
