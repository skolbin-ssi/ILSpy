﻿// Copyright (c) 2019 Siegfried Pammer
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Xml.Linq;

using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.IL.Transforms;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

using ILSpy.BamlDecompiler.Xaml;

namespace ILSpy.BamlDecompiler.Rewrite
{
	internal class ConnectionIdRewritePass : IRewritePass
	{
		static readonly TopLevelTypeName componentConnectorTypeName
			= new TopLevelTypeName("System.Windows.Markup", "IComponentConnector");
		static readonly TopLevelTypeName styleConnectorTypeName
			= new TopLevelTypeName("System.Windows.Markup", "IStyleConnector");

		public void Run(XamlContext ctx, XDocument document)
		{
			var mappings = DecompileEventMappings(ctx, document);
			ProcessConnectionIds(document.Root, mappings);
		}

		static void ProcessConnectionIds(XElement element,
			List<(LongSet key, EventRegistration[] value)> eventMappings)
		{
			foreach (var child in element.Elements())
				ProcessConnectionIds(child, eventMappings);

			foreach (var annotation in element.Annotations<BamlConnectionId>())
			{
				int index;
				if ((index = eventMappings.FindIndex(item => item.key.Contains(annotation.Id))) > -1)
				{
					foreach (var entry in eventMappings[index].value)
					{
						string xmlns = ""; // TODO : implement xmlns resolver!
						var type = element.Annotation<XamlType>();
						if (type?.TypeNamespace + "." + type?.TypeName == "System.Windows.Style")
						{
							element.Add(new XElement(type.Namespace + "EventSetter",
								new XAttribute("Event", entry.EventName),
								new XAttribute("Handler", entry.MethodName)));
						}
						else
						{
							element.Add(new XAttribute(xmlns + entry.EventName, entry.MethodName));
						}
					}
				}
			}
		}

		List<(LongSet, EventRegistration[])> DecompileEventMappings(XamlContext ctx, XDocument document)
		{
			var result = new List<(LongSet, EventRegistration[])>();

			var xClass = document.Root
				.Elements().First()
				.Attribute(ctx.GetKnownNamespace("Class", XamlContext.KnownNamespace_Xaml));
			if (xClass == null)
				return result;

			var type = ctx.TypeSystem.FindType(new FullTypeName(xClass.Value)).GetDefinition();
			if (type == null)
				return result;

			DecompileEventMappings(ctx, result, componentConnectorTypeName, type);
			DecompileEventMappings(ctx, result, styleConnectorTypeName, type);

			return result;
		}

		void DecompileEventMappings(XamlContext ctx, List<(LongSet, EventRegistration[])> result,
			FullTypeName connectorTypeName, ITypeDefinition type)
		{
			var connectorInterface = ctx.TypeSystem.FindType(connectorTypeName).GetDefinition();
			if (connectorInterface == null)
				return;
			var connect = connectorInterface.GetMethods(m => m.Name == "Connect").SingleOrDefault();

			IMethod method = null;
			MethodDefinition metadataEntry = default;
			var module = ctx.TypeSystem.MainModule.PEFile;

			foreach (IMethod m in type.Methods)
			{
				if (m.ExplicitlyImplementedInterfaceMembers.Any(md => md.MemberDefinition.Equals(connect)))
				{
					method = m;
					metadataEntry = module.Metadata
						.GetMethodDefinition((MethodDefinitionHandle)method.MetadataToken);
					break;
				}
			}

			if (method == null || metadataEntry.RelativeVirtualAddress <= 0)
				return;

			var body = module.Reader.GetMethodBody(metadataEntry.RelativeVirtualAddress);
			var genericContext = new GenericContext(
				classTypeParameters: method.DeclaringType?.TypeParameters,
				methodTypeParameters: method.TypeParameters);

			// decompile method and optimize the switch
			var ilReader = new ILReader(ctx.TypeSystem.MainModule);
			var function = ilReader.ReadIL((MethodDefinitionHandle)method.MetadataToken, body, genericContext,
				ILFunctionKind.TopLevelFunction, ctx.CancellationToken);

			var context = new ILTransformContext(function, ctx.TypeSystem, null) {
				CancellationToken = ctx.CancellationToken
			};
			function.RunTransforms(CSharpDecompiler.GetILTransforms(), context);

			var block = function.Body.Children.OfType<Block>().First();
			var ilSwitch = block.Descendants.OfType<SwitchInstruction>().FirstOrDefault();

			var events = new List<EventRegistration>();
			if (ilSwitch != null)
			{
				foreach (var section in ilSwitch.Sections)
				{
					events.Clear();
					FindEvents(section.Body, events);
					if (events.Count > 0)
					{
						result.Add((section.Labels, events.ToArray()));
					}
				}
			}
			else
			{
				foreach (var ifInst in function.Descendants.OfType<IfInstruction>())
				{
					if (!(ifInst.Condition is Comp comp))
						continue;
					if (comp.Kind != ComparisonKind.Inequality && comp.Kind != ComparisonKind.Equality)
						continue;
					if (!comp.Right.MatchLdcI4(out int id))
						continue;
					var inst = comp.Kind == ComparisonKind.Inequality
						? ifInst.FalseInst
						: ifInst.TrueInst;
					events.Clear();
					FindEvents(inst, events);
					if (events.Count > 0)
					{
						result.Add((new LongSet(id), events.ToArray()));
					}
				}
			}
		}

		void FindEvents(ILInstruction inst, List<EventRegistration> events)
		{
			switch (inst)
			{
				case Block b:
					if (MatchEventSetterCreation(b, out var @event))
					{
						events.Add(@event);
						break;
					}
					foreach (var node in b.Instructions)
					{
						if (MatchSimpleEventRegistration(node, out @event))
							events.Add(@event);
					}
					break;
				case Branch br:
					FindEvents(br.TargetBlock, events);
					break;
				default:
					if (MatchSimpleEventRegistration(inst, out @event))
						events.Add(@event);
					break;
			}
		}

		// stloc v(newobj EventSetter..ctor())
		// callvirt set_Event(ldloc v, ldsfld eventName)
		// callvirt set_Handler(ldloc v, newobj RoutedEventHandler..ctor(ldloc this, ldftn eventHandler))
		// callvirt Add(callvirt get_Setters(castclass System.Windows.Style(ldloc target)), ldloc v)
		// leave IL_0007 (nop)
		bool MatchEventSetterCreation(Block b, out EventRegistration @event)
		{
			@event = null;
			var instr = b.Instructions;
			if (instr.Count != 5 || !b.FinalInstruction.MatchNop())
				return false;
			// stloc v(newobj EventSetter..ctor())
			if (!instr.ElementAt(0).MatchStLoc(out var v, out var initializer))
				return false;
			if (!(initializer is NewObj newObj
				&& newObj.Method.DeclaringType.FullName == "System.Windows.EventSetter"
				&& newObj.Arguments.Count == 0))
			{
				return false;
			}
			//callvirt set_Event(ldloc v, ldsfld eventName)
			if (!(instr.ElementAt(1) is CallVirt setEventCall && setEventCall.Arguments.Count == 2))
				return false;
			if (!setEventCall.Method.IsAccessor)
				return false;
			if (!setEventCall.Arguments[0].MatchLdLoc(v))
				return false;
			if (setEventCall.Method.Name != "set_Event")
				return false;
			if (!setEventCall.Arguments[1].MatchLdsFld(out var eventField))
				return false;
			string eventName = eventField.Name;
			if (eventName.EndsWith("Event"))
			{
				eventName = eventName.Remove(eventName.Length - "Event".Length);
			}
			// callvirt set_Handler(ldloc v, newobj RoutedEventHandler..ctor(ldloc this, ldftn eventHandler))
			if (!(instr.ElementAt(2) is CallVirt setHandlerCall && setHandlerCall.Arguments.Count == 2))
				return false;
			if (!setHandlerCall.Method.IsAccessor)
				return false;
			if (!setHandlerCall.Arguments[0].MatchLdLoc(v))
				return false;
			if (setHandlerCall.Method.Name != "set_Handler")
				return false;
			if (!MatchEventHandlerCreation(setHandlerCall.Arguments[1], out string handlerName))
				return false;
			@event = new EventRegistration { EventName = eventName, MethodName = handlerName };
			// callvirt Add(callvirt get_Setters(castclass System.Windows.Style(ldloc target)), ldloc v)
			if (!(instr.ElementAt(3) is CallVirt addCall && addCall.Arguments.Count == 2))
				return false;
			if (addCall.Method.Name != "Add")
				return false;
			if (!(addCall.Arguments[0] is CallVirt getSettersCall && getSettersCall.Arguments.Count == 1))
				return false;
			if (!getSettersCall.Method.IsAccessor)
				return false;
			if (getSettersCall.Method.Name != "get_Setters")
				return false;
			if (!getSettersCall.Arguments[0].MatchCastClass(out var arg, out var type))
				return false;
			if (type.FullName != "System.Windows.Style")
				return false;
			if (!(arg.MatchLdLoc(out var t) && t.Kind == VariableKind.Parameter && t.Index == 1))
				return false;
			if (!addCall.Arguments[1].MatchLdLoc(v))
				return false;
			return true;
		}

		bool MatchSimpleEventRegistration(ILInstruction inst, out EventRegistration @event)
		{
			@event = null;
			if (!(inst is CallInstruction call) || call.OpCode == OpCode.NewObj)
				return false;

			if (!IsAddEvent(call, out string eventName, out string handlerName)
				&& !IsAddAttachedEvent(call, out eventName, out handlerName))
			{
				return false;
			}

			@event = new EventRegistration { EventName = eventName, MethodName = handlerName };
			return true;
		}

		bool IsAddAttachedEvent(CallInstruction call, out string eventName, out string handlerName)
		{
			eventName = "";
			handlerName = "";

			if (call.Arguments.Count == 3)
			{
				var addMethod = call.Method;
				if (addMethod.Name != "AddHandler" || addMethod.Parameters.Count != 2)
					return false;
				if (!call.Arguments[1].MatchLdsFld(out IField field))
					return false;
				eventName = field.DeclaringType.Name + "." + field.Name;
				if (eventName.EndsWith("Event", StringComparison.Ordinal)
					&& eventName.Length > "Event".Length)
				{
					eventName = eventName.Remove(eventName.Length - "Event".Length);
				}
				return MatchEventHandlerCreation(call.Arguments[2], out handlerName);
			}

			return false;
		}

		bool IsAddEvent(CallInstruction call, out string eventName, out string handlerName)
		{
			eventName = "";
			handlerName = "";

			if (call.Arguments.Count == 2)
			{
				var addMethod = call.Method;
				if (!addMethod.Name.StartsWith("add_", StringComparison.Ordinal)
					|| addMethod.Parameters.Count != 1)
				{
					return false;
				}
				eventName = addMethod.Name.Substring("add_".Length);
				return MatchEventHandlerCreation(call.Arguments[1], out handlerName);
			}

			return false;
		}

		bool MatchEventHandlerCreation(ILInstruction inst, out string handlerName)
		{
			handlerName = "";
			if (!(inst is NewObj newObj) || newObj.Arguments.Count != 2)
				return false;
			var ldftn = newObj.Arguments[1];
			if (ldftn.OpCode != OpCode.LdFtn && ldftn.OpCode != OpCode.LdVirtFtn)
				return false;
			handlerName = ((IInstructionWithMethodOperand)ldftn).Method.Name;
			handlerName = XamlUtils.EscapeName(handlerName);
			return true;
		}
	}
}
