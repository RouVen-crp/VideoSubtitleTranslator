using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 负责在程序启动时扫描带有 <see cref="PipelineStepAttribute"/> 的类型，
/// 并根据“步骤名 + 实现名”解析到具体实现类型。
/// </summary>
public sealed class PipelineRegistry
{
    private readonly Dictionary<(string step, string implementation), Type> _implementationTypes =
        new();

    private readonly Dictionary<string, PipelineStepAttribute> _stepLevelAttributes =
        new(StringComparer.OrdinalIgnoreCase);

    public PipelineRegistry(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();
        Discover(assembly);
    }

    private void Discover(Assembly assembly)
    {
        var types = assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsGenericType && typeof(IPipelineStep).IsAssignableFrom(t));

        foreach (var type in types)
        {
            var attr = type.GetCustomAttribute<PipelineStepAttribute>(inherit: false);
            if (attr is null) continue;

            // 步骤层：抽象基类，Implementation 为空
            if (type.IsAbstract && string.IsNullOrWhiteSpace(attr.Implementation))
            {
                _stepLevelAttributes[attr.Step] = attr;
                continue;
            }

            // 实现层：具体类，必须声明 Implementation，且应继承自对应步骤层抽象类
            if (!string.IsNullOrWhiteSpace(attr.Implementation))
            {
                // 简单校验：若存在对应步骤层抽象类，则要求当前类型继承自它
                if (_stepLevelAttributes.TryGetValue(attr.Step, out var _))
                {
                    var baseAttr = type.BaseType?.GetCustomAttribute<PipelineStepAttribute>(inherit: false);
                    if (baseAttr is null || !string.Equals(baseAttr.Step, attr.Step, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"类型 {type.FullName} 标记为步骤 \"{attr.Step}\" 的实现 \"{attr.Implementation}\", " +
                            "但其基类未正确标记对应的步骤层注解。");
                    }
                }

                var key = (attr.Step, attr.Implementation);
                _implementationTypes[key] = type;
            }
        }
    }

    /// <summary>
    /// 根据“步骤名 + 实现名”创建一个具体的步骤实例。
    /// <para>要求实现类：</para>
    /// <list type="bullet">
    /// <item><description>实现 <see cref="IPipelineStep"/>；</description></item>
    /// <item><description>带有 <see cref="PipelineStepAttribute"/> 注解；</description></item>
    /// <item><description>拥有无参构造函数；</description></item>
    /// <item><description>设计为无状态（不依赖构造参数与内部可变字段）。</description></item>
    /// </list>
    /// </summary>
    public IPipelineStep Resolve(string step, string implementation)
    {
        if (string.IsNullOrWhiteSpace(step)) throw new ArgumentException("step 不能为空", nameof(step));
        if (string.IsNullOrWhiteSpace(implementation)) throw new ArgumentException("implementation 不能为空", nameof(implementation));

        if (!_implementationTypes.TryGetValue((step, implementation), out var type))
        {
            throw new InvalidOperationException($"未找到步骤 \"{step}\" 的实现 \"{implementation}\"。");
        }

        if (Activator.CreateInstance(type) is not IPipelineStep instance)
        {
            throw new InvalidOperationException(
                $"无法创建 {type.FullName} 的实例，它必须实现 IPipelineStep 且拥有无参构造函数。");
        }

        return instance;
    }

    /// <summary>
    /// 按“实现层优先，其次步骤层”的规则，返回指定实现的入参与约束。
    /// </summary>
    public string[]? GetRequiredKeys(string step, string implementation)
    {
        if (_implementationTypes.TryGetValue((step, implementation), out var type))
        {
            var implAttr = type.GetCustomAttribute<PipelineStepAttribute>(inherit: false);
            if (implAttr?.RequiredKeys is { Length: > 0 } keysFromImpl)
            {
                return keysFromImpl;
            }
        }

        if (_stepLevelAttributes.TryGetValue(step, out var stepAttr) &&
            stepAttr.RequiredKeys is { Length: > 0 } keysFromStep)
        {
            return keysFromStep;
        }

        return null;
    }
}

