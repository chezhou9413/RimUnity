using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RimUnity.Editor
{
    /// <summary>Opt a parameterless processor into every editor and batch export.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class RimUnityExportProcessorAttribute : Attribute { }

    public interface IExportProcessor
    {
        int Order { get; }
        bool Matches(ExportContext context);
        void BeforeExport(ExportContext context);
        void AfterExport(ExportContext context);
    }

    public abstract class ExportProcessor : IExportProcessor
    {
        public virtual int Order => 0;
        public virtual bool Matches(ExportContext context) => true;
        public virtual void BeforeExport(ExportContext context) { }
        public virtual void AfterExport(ExportContext context) { }
    }

    /// <summary>Attach functions to ExportOptions.processors for one export batch.</summary>
    public sealed class DelegateExportProcessor : ExportProcessor
    {
        public int order;
        public Predicate<ExportContext> filter;
        public Action<ExportContext> before, after;
        public override int Order => order;
        public override bool Matches(ExportContext context) => filter == null || filter(context);
        public override void BeforeExport(ExportContext context) => before?.Invoke(context);
        public override void AfterExport(ExportContext context) => after?.Invoke(context);
    }

    /// <summary>One source asset/variant. Cleanup runs in reverse order, even on failure.</summary>
    public sealed class ExportContext
    {
        private readonly Stack<Action> cleanup = new Stack<Action>();
        private bool acceptingCleanup = true;
        public Object Source { get; }
        public Object ExportAsset { get; set; }
        public string SourcePath { get; }
        public string ReferencePath { get; }
        public string ResourceId { get; internal set; }
        public string OutputDirectory { get; }
        public string OutputFile { get; internal set; }
        public bool IsUI { get; }
        public bool IsCameraUI { get; }
        public bool Succeeded { get; internal set; }
        public Exception Error { get; internal set; }

        internal ExportContext(Object source, string sourcePath, string referencePath, string resourceId,
            string outputDirectory, bool ui, bool cameraUi)
        {
            Source = ExportAsset = source; SourcePath = sourcePath; ReferencePath = referencePath;
            ResourceId = resourceId; OutputDirectory = outputDirectory; IsUI = ui; IsCameraUI = cameraUi;
        }

        /// <summary>Register restoration BEFORE changing a source object. Do not save/reimport it.</summary>
        public void DeferCleanup(Action action)
        {
            if (!acceptingCleanup) throw new InvalidOperationException("导出清理已经开始，不能继续登记清理操作。");
            cleanup.Push(action ?? throw new ArgumentNullException(nameof(action)));
        }

        /// <summary>Create an owned temporary copy; its source identity stays unchanged.</summary>
        public T CreateExportCopy<T>() where T : Object
        {
            if (!(ExportAsset is T current)) throw new InvalidOperationException("当前导出资源不是 " + typeof(T).Name);
            T copy = Object.Instantiate(current);
            DeferCleanup(() => Object.DestroyImmediate(copy));
            copy.name = Source.name; copy.hideFlags = HideFlags.HideAndDontSave;
            ExportAsset = copy;
            return copy;
        }

        internal void Cleanup(List<Exception> errors)
        {
            acceptingCleanup = false;
            while (cleanup.Count > 0)
                try { cleanup.Pop()(); }
                catch (Exception error) { errors.Add(new InvalidOperationException("导出清理失败：" + SourcePath, error)); }
        }
    }

    //Batch-owned snapshot: no static asset references or registrations survive an export.
    internal sealed class ExportProcessorPipeline
    {
        private readonly IExportProcessor[] processors;
        internal ExportProcessorPipeline(ExportOptions options)
        {
            var all = new List<IExportProcessor>();
            if (options.discoverProcessors)
                foreach (Type type in TypeCache.GetTypesWithAttribute<RimUnityExportProcessorAttribute>().OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    if (type.IsAbstract || type.ContainsGenericParameters || !typeof(IExportProcessor).IsAssignableFrom(type))
                        throw new InvalidOperationException("导出处理器必须是非抽象 IExportProcessor：" + type.FullName);
                    try { all.Add((IExportProcessor)Activator.CreateInstance(type)); }
                    catch (Exception error) { throw new InvalidOperationException("无法创建导出处理器（需要公开无参构造）：" + type.FullName, error); }
                }
            foreach (var processor in options.processors ?? Array.Empty<IExportProcessor>())
                all.Add(processor ?? throw new ArgumentException("导出处理器列表包含 null。"));
            processors = all.OrderBy(p => p.Order).ThenBy(p => p.GetType().FullName, StringComparer.Ordinal).ToArray();
        }

        internal T Run<T>(ExportContext context, Func<ExportContext, T> write)
        {
            var errors = new List<Exception>();
            var entered = new List<IExportProcessor>();
            T result = default;
            try
            {
                foreach (var processor in processors)
                {
                    if (!processor.Matches(context)) continue;
                    entered.Add(processor);
                    processor.BeforeExport(context);
                    if (context.ExportAsset == null || context.ExportAsset.GetType() != context.Source.GetType())
                        throw new InvalidOperationException("处理器不能删除资源或改变资源类型：" + context.SourcePath);
                }
                result = write(context);
                //AfterExport observes a written resource, before cleanup and catalog publication.
                context.Succeeded = true;
                foreach (var processor in entered) processor.AfterExport(context);
            }
            catch (Exception error) { context.Succeeded = false; context.Error = error; errors.Add(error); }
            finally { context.Cleanup(errors); }
            if (errors.Count > 0)
            {
                context.Succeeded = false;
                var failure = new AggregateException("资源导出或清理失败：" + context.SourcePath + " (" + context.ReferencePath + ")", errors);
                context.Error = failure;
                throw failure;
            }
            return result;
        }
    }
}
