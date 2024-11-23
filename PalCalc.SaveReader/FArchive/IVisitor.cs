﻿using PalCalc.SaveReader.FArchive.Custom;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace PalCalc.SaveReader.FArchive
{
    // generic visitor
    public abstract class IVisitor
    {
        public IVisitor(string matchedPath) : this(matchedPath, null)
        {
        }

        public IVisitor(string basePath, string subPath)
        {
            MatchedBasePath = basePath;
            MatchedSubPath = subPath;

            MatchedPath = subPath == null
                ? MatchedBasePath
                : MatchedBasePath + "." + MatchedSubPath;
        }

        // must be non-null
        public string MatchedBasePath { get; private set; }

        // may be null
        public virtual string MatchedSubPath { get; protected set; }

        public string MatchedPath { get; }

        public virtual bool Matches(string path) => MatchedPath == path;

        public virtual bool IsDone => false;

        // Called when the Visitor has reached the end of its matched scope and will no longer be tracked
        // in the list of visitors (only for visitors returned as results of another `Visit` call)
        public virtual void Exit()
        {

        }

        public virtual void VisitByte(string path, byte value) { }
        public virtual void VisitInt(string path, int value) { }
        public virtual void VisitUInt16(string path, ushort value) { }
        public virtual void VisitUInt32(string path, uint value) { }
        public virtual void VisitInt64(string path, long value) { }
        public virtual void VisitDouble(string path, int value) { } // ??????????????
        public virtual void VisitFloat(string path, float value) { }
        public virtual void VisitString(string path, string value) { }
        public virtual void VisitBool(string path, bool value) { }
        public virtual void VisitGuid(string path, Guid guid) { }
        public virtual void VisitDateTime(string path, ulong value) { }
        public virtual void VisitVector(string path, VectorLiteral value) { }
        public virtual void VisitQuaternion(string path, QuaternionLiteral value) { }
        public virtual void VisitLinearColor(string path, LinearColorLiteral value) { }

        public virtual void VisitLiteralProperty(string path, LiteralProperty prop) { }

        public virtual IEnumerable<IVisitor> VisitEnumPropertyBegin(string path, EnumPropertyMeta meta) => Enumerable.Empty<IVisitor>();
        public virtual IEnumerable<IVisitor> VisitStructPropertyBegin(string path, StructPropertyMeta meta) => Enumerable.Empty<IVisitor>();
        public virtual IEnumerable<IVisitor> VisitArrayPropertyBegin(string path, ArrayPropertyMeta meta) => Enumerable.Empty<IVisitor>();
        public virtual IEnumerable<IVisitor> VisitMapPropertyBegin(string path, MapPropertyMeta meta) => Enumerable.Empty<IVisitor>();

        // meta is at the end
        public virtual IEnumerable<IVisitor> VisitCharacterPropertyBegin(string path) => Enumerable.Empty<IVisitor>();

        public virtual IEnumerable<IVisitor> VisitArrayEntryBegin(string path, int index, ArrayPropertyMeta meta) => Enumerable.Empty<IVisitor>();
        public virtual IEnumerable<IVisitor> VisitMapEntryBegin(string path, int index, MapPropertyMeta meta) => Enumerable.Empty<IVisitor>();

        public virtual void VisitEnumPropertyEnd(string path, EnumPropertyMeta meta) { }
        public virtual void VisitStructPropertyEnd(string path, StructPropertyMeta meta) { }
        public virtual void VisitArrayPropertyEnd(string path, ArrayPropertyMeta meta) { }
        public virtual void VisitMapPropertyEnd(string path, MapPropertyMeta meta) { }

        public virtual void VisitCharacterPropertyEnd(string path, CharacterDataPropertyMeta meta) { }

        public virtual void VisitArrayEntryEnd(string path, int index, ArrayPropertyMeta meta) { }
        public virtual void VisitMapEntryEnd(string path, int index, MapPropertyMeta meta) { }

        // no start/end, these are custom-serialized types without any property name dictionaries, so they're
        // deserialized as a whole
        public virtual void VisitCharacterGroupProperty(string path, GroupDataProperty property) { }
        public virtual void VisitBaseCampProperty(string path, BaseCampDataProperty property) { }
        public virtual void VisitWorkerDirectorProperty(string path, WorkerDirectorDataProperty property) { }
        public virtual void VisitMapModelProperty(string path, MapModelDataProperty property) { }
        public virtual void VisitCharacterContainerProperty(string path, CharacterContainerDataProperty property) { }
    }

    // a visitor that implements `IsDone` if the requested path has been visited at least once and the most recent path does not
    // start with the requested path
    // 
    // NOTE: not worth it atm due to performance hit in `Matches` hot path
    public abstract class IScopedVisitor : IVisitor
    {
        public IScopedVisitor(string matchedPath) : base(matchedPath)
        {
            didStartReading = false;
            didStopReading = false;
        }

        bool didStartReading;
        bool didStopReading;

        public override bool IsDone => didStopReading;

        public override bool Matches(string path)
        {
            if (didStartReading)
            {
                if (!didStopReading)
                {
                    didStopReading = !path.StartsWith(MatchedPath);
                    if (didStopReading) Console.WriteLine("{0} stopped reading", GetType().Name);
                }
            }
            else
            {
                didStartReading = path.StartsWith(MatchedPath);
                if (didStartReading) Console.WriteLine("{0} started reading", GetType().Name);
            }

            return base.Matches(path);
        }
    }

    public class ValueCollectingVisitor : IVisitor
    {
        private static ILogger logger = Log.ForContext<ValueCollectingVisitor>();

        string[] propertiesToCollect;
        Dictionary<string, object> collectedValues = new Dictionary<string, object>();

        public ValueCollectingVisitor(IVisitor parent, params string[] propertySubPaths) : this(parent.MatchedPath, propertySubPaths)
        {
        }

        public ValueCollectingVisitor(string basePath, params string[] propertySubPaths) : base(basePath)
        {
            logger.Verbose("init");
            propertiesToCollect = propertySubPaths;
        }

        public event Action<Dictionary<string, object>> OnExit;
        public ValueCollectingVisitor WithOnExit(Action<Dictionary<string, object>> cb)
        {
            OnExit += cb;
            return this;
        }

        public override bool Matches(string path) => path.StartsWith(MatchedBasePath);

        public Dictionary<string, object> Result => collectedValues;

        private void VisitValue(string path, object value)
        {
            var propPart = path.Substring(MatchedBasePath.Length);
            if (propertiesToCollect.Length > 0 && !propertiesToCollect.Contains(propPart)) return;

            logger.Verbose("collected {value} at {path}", value, path);
            if (collectedValues.ContainsKey(propPart))
            {
                logger.Warning("value was already collected for {path}, overwriting with new {value}", path, value);
#if DEBUG
                Debugger.Break();
#endif
            }
            collectedValues[propPart] = value;
        }

        public override void VisitBool(string path, bool value) => VisitValue(path, value);
        public override void VisitDouble(string path, int value) => VisitValue(path, value);
        public override void VisitFloat(string path, float value) => VisitValue(path, value);
        public override void VisitInt(string path, int value) => VisitValue(path, value);
        public override void VisitGuid(string path, Guid guid) => VisitValue(path, guid);
        public override void VisitInt64(string path, long value) => VisitValue(path, value);
        public override void VisitString(string path, string value) => VisitValue(path, value);

        public override void VisitDateTime(string path, ulong value) => VisitValue(path, value);
        public override void VisitVector(string path, VectorLiteral value) => VisitValue(path, value);
        public override void VisitQuaternion(string path, QuaternionLiteral value) => VisitValue(path, value);
        public override void VisitLinearColor(string path, LinearColorLiteral value) => VisitValue(path, value);

        public override void VisitUInt16(string path, ushort value) => VisitValue(path, value);
        public override void VisitUInt32(string path, uint value) => VisitValue(path, value);
        public override void VisitByte(string path, byte value) => VisitValue(path, value);

        public override void Exit()
        {
            logger.Verbose("exit");
            OnExit?.Invoke(collectedValues);
            OnExit = null;
            collectedValues = null;
        }
    }

    public class ValueEmittingVisitor : IVisitor
    {
        private static ILogger logger = Log.ForContext<ValueEmittingVisitor>();

        string[] propertiesToEmit;

        public ValueEmittingVisitor(IVisitor parent, params string[] propertySubPaths) : this(parent.MatchedPath, propertySubPaths)
        {
        }

        public ValueEmittingVisitor(string basePath, params string[] propertySubPaths) : base(basePath)
        {
            logger.Verbose("init");
            propertiesToEmit = propertySubPaths;
        }

        public event Action<string, object> OnValue;
        public ValueEmittingVisitor WithOnValue(Action<string, object> cb)
        {
            OnValue += cb;
            return this;
        }

        public event Action OnExit;
        public ValueEmittingVisitor WithOnExit(Action cb)
        {
            OnExit += cb;
            return this;
        }

        public override bool Matches(string path) => path.StartsWith(MatchedBasePath);

        private void VisitValue(string path, object value)
        {
            var propPart = path.Substring(MatchedBasePath.Length);
            if (propertiesToEmit.Length > 0 && !propertiesToEmit.Contains(propPart)) return;

            logger.Verbose("emitting {value} from path {path}", value, path);
            OnValue?.Invoke(propPart, value);
        }

        public override void VisitBool(string path, bool value) => VisitValue(path, value);
        public override void VisitDouble(string path, int value) => VisitValue(path, value);
        public override void VisitFloat(string path, float value) => VisitValue(path, value);
        public override void VisitInt(string path, int value) => VisitValue(path, value);
        public override void VisitGuid(string path, Guid guid) => VisitValue(path, guid);
        public override void VisitInt64(string path, long value) => VisitValue(path, value);
        public override void VisitString(string path, string value) => VisitValue(path, value);
        public override void VisitUInt16(string path, ushort value) => VisitValue(path, value);
        public override void VisitUInt32(string path, uint value) => VisitValue(path, value);
        public override void VisitByte(string path, byte value) => VisitValue(path, value);

        public override void Exit()
        {
            logger.Verbose("exit");
            OnExit?.Invoke();
            OnExit = null;
            OnValue = null;
        }
    }

    public class PropertyEmittingVisitor<T> : IVisitor where T : class, ICustomProperty
    {
        string[] propertiesToEmit;

        public PropertyEmittingVisitor(IVisitor parent, params string[] propertySubPaths) : this(parent.MatchedPath, propertySubPaths)
        {
        }

        public PropertyEmittingVisitor(string basePath, params string[] propertySubPaths) : base(basePath)
        {
            propertiesToEmit = propertySubPaths;
        }

        public event Action<string, T> OnValue;
        public PropertyEmittingVisitor<T> WithOnValue(Action<string, T> cb)
        {
            OnValue += cb;
            return this;
        }

        public event Action OnExit;
        public PropertyEmittingVisitor<T> WithOnExit(Action cb)
        {
            OnExit += cb;
            return this;
        }

        public override bool Matches(string path) => path.StartsWith(MatchedBasePath);

        private void VisitValue<V>(string path, V value) where V : class, ICustomProperty
        {
            var propPart = path.Substring(MatchedBasePath.Length);
            if (propertiesToEmit.Length > 0 && !propertiesToEmit.Contains(propPart)) return;
            if (value is not T) return;

            OnValue?.Invoke(path, value as T);
        }

        public override void VisitBaseCampProperty(string path, BaseCampDataProperty property) => VisitValue(path, property);
        public override void VisitCharacterGroupProperty(string path, GroupDataProperty property) => VisitValue(path, property);
        public override void VisitWorkerDirectorProperty(string path, WorkerDirectorDataProperty property) => VisitValue(path, property);
        public override void VisitMapModelProperty(string path, MapModelDataProperty property) => VisitValue(path, property);
    }
}
