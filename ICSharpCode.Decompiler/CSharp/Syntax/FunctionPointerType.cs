﻿// 
// FullTypeName.cs
//
// Author:
//       Mike Krüger <mkrueger@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using ICSharpCode.Decompiler.CSharp.Resolver;
using ICSharpCode.Decompiler.CSharp.TypeSystem;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.CSharp.Syntax
{
	public class FunctionPointerType : AstType
	{
		public static readonly TokenRole PointerRole = new TokenRole("*");
		public static readonly Role<Identifier> CallingConventionRole = new Role<Identifier>("Target", Identifier.Null);
		
		public string CallingConvention {
			get {
				return GetChildByRole (CallingConventionRole).Name;
			}
			set {
				SetChildByRole (CallingConventionRole, Identifier.Create (value));
			}
		}

		public Identifier CallingConventionIdentifier => GetChildByRole(CallingConventionRole);
		
		public AstNodeCollection<AstType> TypeArguments {
			get { return GetChildrenByRole (Roles.TypeArgument); }
		}
		
		public override void AcceptVisitor (IAstVisitor visitor)
		{
			visitor.VisitFunctionPointerType(this);
		}
		
		public override T AcceptVisitor<T> (IAstVisitor<T> visitor)
		{
			return visitor.VisitFunctionPointerType(this);
		}
		
		public override S AcceptVisitor<T, S> (IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitFunctionPointerType(this, data);
		}
		
		protected internal override bool DoMatch(AstNode other, PatternMatching.Match match)
		{
			return other is FunctionPointerType o && MatchString(this.CallingConvention, o.CallingConvention)
				&& this.TypeArguments.DoMatch(o.TypeArguments, match);
		}

		public override ITypeReference ToTypeReference(NameLookupMode lookupMode, InterningProvider interningProvider = null)
		{
			throw new NotImplementedException();
		}
	}
}

