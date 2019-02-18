﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Util;
using CppSharp.AST;
using CppSharp.AST.Extensions;
using CppSharp.Generators.CSharp;

namespace CppSharp.Generators
{
    public abstract class CodeGenerator : BlockGenerator, IAstVisitor<bool>
    {
        public BindingContext Context { get; }

        public DriverOptions Options => Context.Options;

        public List<TranslationUnit> TranslationUnits { get; }

        public TranslationUnit TranslationUnit => TranslationUnits[0];

        public abstract string FileExtension { get; }

        public virtual string FilePath =>
            $"{TranslationUnit.FileNameWithoutExtension}.{FileExtension}";

        /// <summary>
        /// Gets the comment style kind for regular comments.
        /// </summary>
        public virtual CommentKind CommentKind
        {
            get
            {
                if (!Options.CommentKind.HasValue)
                    return CommentKind.BCPL;
                
                return Options.CommentKind.Value;
            }
        } 

        /// <summary>
        /// Gets the comment style kind for documentation comments.
        /// </summary>
        public virtual CommentKind DocumentationCommentKind => CommentKind.BCPLSlash;

        public ISet<object> Visited { get; } = new HashSet<object>();

        public AstVisitorOptions VisitOptions { get; } = new AstVisitorOptions();

        protected CodeGenerator(BindingContext context)
        {
            Context = context;
        }

        protected CodeGenerator(BindingContext context, TranslationUnit unit)
            : this(context, new List<TranslationUnit> { unit })
        {
        }

        protected CodeGenerator(BindingContext context, IEnumerable<TranslationUnit> units)
        {
            Context = context;
            TranslationUnits = new List<TranslationUnit>(units);
        }

        public abstract void Process();

        public virtual void GenerateFilePreamble(CommentKind kind, string generatorName = "CppSharp")
        {
            var lines = new List<string>
            {
                "----------------------------------------------------------------------------",
                "<auto-generated>",
                $"This is autogenerated code by {generatorName}.",
                "Do not edit this file or all your changes will be lost after re-generation.",
                "</auto-generated>",
                "----------------------------------------------------------------------------"
            };

            PushBlock(BlockKind.Header);
            GenerateMultiLineComment(lines, kind);
            PopBlock();
        }

        #region Declaration generation

        public virtual void GenerateDeclarationCommon(Declaration decl)
        {
            if (decl.Comment != null)
                GenerateComment(decl.Comment);

            GenerateDebug(decl);
        }

        public virtual void GenerateDebug(Declaration decl)
        {
            if (Options.GenerateDebugOutput && !string.IsNullOrWhiteSpace(decl.DebugText))
            {
                var debugText = decl.DebugText;
                debugText = Regex.Replace(debugText.Trim(), "\r?\n", "\n// DEBUG: ");
                WriteLine($"// DEBUG: {debugText}");
            }
        }

        #endregion

        #region Identifiers generation

        public virtual string SafeIdentifier(string id) => id;

        public virtual string GeneratedIdentifier(string id) => Generator.GeneratedIdentifier(id);

        public virtual string AccessIdentifier(AccessSpecifier accessSpecifier)
        {
            return Helpers.GetAccess(accessSpecifier);
        }

        #endregion

        #region Comment generation

        public virtual void GenerateSummary(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return;

            var lines = new List<string>
            {
                "<summary>",
                $"{comment}",
                "</summary>"
            };

            GenerateMultiLineComment(lines, DocumentationCommentKind);
        }

        public virtual void GenerateInlineSummary(RawComment comment)
        {
            GenerateComment(comment);
        }

        public virtual void GenerateComment(RawComment comment)
        {
            if (comment.FullComment != null)
            {
                PushBlock(BlockKind.BlockComment);
                WriteLine(comment.FullComment.CommentToString(DocumentationCommentKind));
                PopBlock();
                return;
            }

            if (string.IsNullOrWhiteSpace(comment.BriefText))
                return;

            var lines = new List<string>();

            if (comment.BriefText.Contains("\n"))
            {
                lines.Add("<summary>");
                foreach (string line in HtmlEncoder.HtmlEncode(comment.BriefText).Split(
                                            Environment.NewLine.ToCharArray()))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    lines.Add($"<para>{line}</para>");
                }
                lines.Add("</summary>");
            }
            else
            {
                lines.Add($"<summary>{comment.BriefText}</summary>");
            }

            GenerateMultiLineComment(lines, CommentKind);
        }

        public virtual void GenerateMultiLineComment(List<string> lines, CommentKind kind)
        {
            PushBlock(BlockKind.BlockComment);
        
            var lineCommentPrologue = Comment.GetLineCommentPrologue(kind);
            if (!string.IsNullOrWhiteSpace(lineCommentPrologue))
                WriteLine("{0}", lineCommentPrologue);

            var multiLineCommentPrologue = Comment.GetMultiLineCommentPrologue(kind);
            foreach (var line in lines)
                WriteLine("{0} {1}", multiLineCommentPrologue, line);

            var lineCommentEpilogue = Comment.GetLineCommentEpilogue(kind);
            if (!string.IsNullOrWhiteSpace(lineCommentEpilogue))
                WriteLine("{0}", lineCommentEpilogue);

            PopBlock();
        }

        #endregion

        #region Enum generation

        public virtual void GenerateEnumItems(Enumeration @enum)
        {
            for (int i = 0; i < @enum.Items.Count; i++)
            {
                var item = @enum.Items[i];
                if (!item.IsGenerated)
                    continue;

                item.Visit(this);
                WriteLine(i == @enum.Items.Count - 1 ? string.Empty : ",");
            }
        }

        public virtual bool VisitEnumItemDecl(Enumeration.Item item)
        {
            if (item.Comment != null)
                GenerateInlineSummary(item.Comment);

            Write(item.Name);

            var @enum = item.Namespace as Enumeration;
            if (item.ExplicitValue)
                Write(" = {0}", @enum.GetItemValueAsString(item));

            return true;
        }

        #endregion

        #region Class generation

        public virtual void GenerateClassSpecifier(Class @class)
        {
        }

        #endregion

        #region Method generation

        public virtual void GenerateMethodSpecifier(Method method, Class @class)
        {
        }

        #endregion

        #region Visitor methods

        public bool AlreadyVisited(CppSharp.AST.Type type)
        {
            return !Visited.Add(type);
        }

        public bool AlreadyVisited(Declaration decl)
        {
            return !Visited.Add(decl);
        }

        public virtual bool VisitDeclaration(Declaration decl)
        {
            return !AlreadyVisited(decl);
        }

        public virtual bool VisitTranslationUnit(TranslationUnit unit)
        {
            return VisitDeclContext(unit);
        }

        public virtual bool VisitDeclContext(DeclarationContext context)
        {
            foreach (var decl in context.Declarations)
                if (decl.IsGenerated)
                    decl.Visit(this);

            return true;
        }

        public virtual bool VisitClassDecl(Class @class)
        {
            return VisitDeclContext(@class);
        }

        public virtual bool VisitFieldDecl(Field field)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitFunctionDecl(Function function)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitMethodDecl(Method method)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitParameterDecl(Parameter parameter)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitTypedefNameDecl(TypedefNameDecl typedef)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitTypedefDecl(TypedefDecl typedef)
        {
            return VisitTypedefNameDecl(typedef);
        }

        public virtual bool VisitTypeAliasDecl(TypeAlias typeAlias)
        {
            return VisitTypedefNameDecl(typeAlias);
        }

        public virtual bool VisitEnumDecl(Enumeration @enum)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitVariableDecl(Variable variable)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitMacroDefinition(MacroDefinition macro)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitNamespace(Namespace @namespace)
        {
            return VisitDeclContext(@namespace);
        }

        public virtual bool VisitEvent(Event @event)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitProperty(Property property)
        {
            if (!VisitDeclaration(property))
                return false;

            if (VisitOptions.VisitPropertyAccessors)
            {
                if (property.GetMethod != null)
                    property.GetMethod.Visit(this);
        
                if (property.SetMethod != null)
                    property.SetMethod.Visit(this);
            }

            return true;
        }

        public virtual bool VisitFriend(Friend friend)
        {
            return true;
        }

        public virtual bool VisitClassTemplateDecl(ClassTemplate template)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitClassTemplateSpecializationDecl(ClassTemplateSpecialization specialization)
        {
            return VisitClassDecl(specialization);
        }

        public virtual bool VisitFunctionTemplateDecl(FunctionTemplate template)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitFunctionTemplateSpecializationDecl(FunctionTemplateSpecialization specialization)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitVarTemplateDecl(VarTemplate template)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitVarTemplateSpecializationDecl(VarTemplateSpecialization template)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitTemplateTemplateParameterDecl(TemplateTemplateParameter templateTemplateParameter)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitTemplateParameterDecl(TypeTemplateParameter templateParameter)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitNonTypeTemplateParameterDecl(NonTypeTemplateParameter nonTypeTemplateParameter)
        {
            throw new NotImplementedException();
        }

        public virtual bool VisitTypeAliasTemplateDecl(TypeAliasTemplate typeAliasTemplate)
        {
            throw new NotImplementedException();
        }

        public bool VisitTagType(TagType tag, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitArrayType(ArrayType array, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitFunctionType(FunctionType function, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitPointerType(PointerType pointer, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitMemberPointerType(MemberPointerType member, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitBuiltinType(BuiltinType builtin, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitTypedefType(TypedefType typedef, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitAttributedType(AttributedType attributed, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitDecayedType(DecayedType decayed, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitTemplateSpecializationType(TemplateSpecializationType template, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitDependentTemplateSpecializationType(DependentTemplateSpecializationType template, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitPrimitiveType(PrimitiveType type, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitDeclaration(Declaration decl, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitTemplateParameterType(TemplateParameterType param, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitTemplateParameterSubstitutionType(TemplateParameterSubstitutionType param, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitInjectedClassNameType(InjectedClassNameType injected, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitDependentNameType(DependentNameType dependent, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitPackExpansionType(PackExpansionType packExpansionType, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitUnaryTransformType(UnaryTransformType unaryTransformType, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitVectorType(VectorType vectorType, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitCILType(CILType type, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        public bool VisitUnsupportedType(UnsupportedType type, TypeQualifiers quals)
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    public static class Helpers
    {
        public static Regex RegexTag = new Regex(@"^(<|</)[a-zA-Z][\w\-]*?>?$");
        public static Regex RegexCommentCommandLeftover = new Regex(@"^\S*");
        public static readonly string InternalStruct = Generator.GeneratedIdentifier("Internal");
        public static readonly string InstanceField = Generator.GeneratedIdentifier("instance");
        public static readonly string InstanceIdentifier = Generator.GeneratedIdentifier("Instance");
        public static readonly string PointerAdjustmentIdentifier = Generator.GeneratedIdentifier("PointerAdjustment");
        public static readonly string ReturnIdentifier = Generator.GeneratedIdentifier("ret");
        public static readonly string DummyIdentifier = Generator.GeneratedIdentifier("dummy");
        public static readonly string TargetIdentifier = Generator.GeneratedIdentifier("target");
        public static readonly string SlotIdentifier = Generator.GeneratedIdentifier("slot");
        public static readonly string PtrIdentifier = Generator.GeneratedIdentifier("ptr");

        public static readonly string OwnsNativeInstanceIdentifier = Generator.GeneratedIdentifier("ownsNativeInstance");

        public static readonly string CreateInstanceIdentifier = Generator.GeneratedIdentifier("CreateInstance");

        public static string GetSuffixForInternal(DeclarationContext @class)
        {
            if (@class == null)
                return string.Empty;

            Class template = null;
            var specialization = @class as ClassTemplateSpecialization ??
                @class.Namespace as ClassTemplateSpecialization;
            if (specialization != null)
            {
                template = specialization.TemplatedDecl.TemplatedClass;
                if (@class != specialization)
                    template = template.Classes.FirstOrDefault(c => c.Name == @class.Name);
            }

            if (template == null || !template.HasDependentValueFieldInLayout())
                return string.Empty;

            if (specialization.Arguments.All(
                a => a.Type.Type?.IsAddress() == true))
                return "_Ptr";

            return GetSuffixFor(specialization);
        }

        public static string GetSuffixFor(Declaration decl)
        {
            var suffixBuilder = new StringBuilder(decl.USR);
            for (int i = 0; i < suffixBuilder.Length; i++)
                if (!char.IsLetterOrDigit(suffixBuilder[i]))
                    suffixBuilder[i] = '_';
            const int maxCSharpIdentifierLength = 480;
            if (suffixBuilder.Length > maxCSharpIdentifierLength)
                return suffixBuilder.Remove(maxCSharpIdentifierLength,
                    suffixBuilder.Length - maxCSharpIdentifierLength).ToString();
            return suffixBuilder.ToString();
        }

        public static string GetAccess(AccessSpecifier accessSpecifier)
        {
            switch (accessSpecifier)
            {
                case AccessSpecifier.Private:
                case AccessSpecifier.Internal:
                    return "internal ";
                case AccessSpecifier.Protected:
                    return "protected ";
                default:
                    return "public ";
            }
        }
    }
}
