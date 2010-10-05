/*
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace HtmlCompressor
{
    public class HtmlCompressorFilter : Stream
    {
        private readonly StringBuilder _responseHtml = new StringBuilder();
        private readonly Stream _sink;

        public HtmlCompressorFilter(Stream sink)
        {
            _sink = sink;
        }

        // The following members of Stream must be overriden.
        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override long Length
        {
            get { return 0; }
        }

        public override long Position { get; set; }

        public override long Seek(long offset, SeekOrigin direction)
        {
            return _sink.Seek(offset, direction);
        }

        public override void SetLength(long length)
        {
            _sink.SetLength(length);
        }

        public override void Close()
        {
            _sink.Close();
        }

        public override void Flush()
        {
            string strHtmlOutput = _responseHtml.ToString();
            //here we can change the content
            strHtmlOutput = new Compression().Compress(strHtmlOutput);

            byte[] data = HttpContext.Current.Response.ContentEncoding.GetBytes(strHtmlOutput);
            _sink.Write(data, 0, data.Length);
            _sink.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _sink.Read(buffer, offset, count);
        }

        // The Write method actually does the filtering.
        public override void Write(byte[] buffer, int offset, int count)
        {
            string strBuffer = HttpContext.Current.Response.ContentEncoding.GetString(buffer, offset, count);
            _responseHtml.Append(strBuffer);
        }
    }

    /// <summary>
    /// Class that compresses given HTML source by removing comments, extra spaces and 
    /// line breaks while preserving content within &lt;pre>, &lt;textarea>, &lt;script> 
    /// and &lt;style> tags.
    ///
    /// @author <a href="mailto:serg472@gmail.com">Sergiy Kovalchuk</a>
    /// @author port to C# by David Murdoch
    /// </summary>
    public class Compression
    {
        //temp replacements for preserved blocks 
        private const string TempCondCommentBlock = "%%%COMPRESS~COND~{0}%%%";
        private const string TempPreBlock = "%%%COMPRESS~PRE~{0}%%%";
        private const string TempTextAreaBlock = "%%%COMPRESS~TEXTAREA~{0}%%%";
        private const string TempScriptBlock = "%%%COMPRESS~SCRIPT~{0}%%%";
        private const string TempStyleBlock = "%%%COMPRESS~STYLE~{0}%%%";
        private const string TempEventBlock = "%%%COMPRESS~EVENT~{0}%%%";
        private const string TempUserBlock = "%%%COMPRESS~USER{{0}}~{0}%%%";

        //compiled regex patterns
        private static readonly Regex CondCommentPattern = new Regex("(<!(?:--)?\\[[^\\]]+?]>)(.*?)(<!\\[[^\\]]+]-->)",
                                                                     RegexOptions.IgnoreCase);

        private static readonly Regex CommentPattern = new Regex("<!--[^\\[].*?-->", RegexOptions.IgnoreCase);
        private static readonly Regex IntertagPattern = new Regex(">\\s+?<", RegexOptions.IgnoreCase);
        private static readonly Regex MultispacePattern = new Regex("\\s{2,}", RegexOptions.IgnoreCase);

        private static readonly Regex TagEndSpacePattern = new Regex("(<(?:[^>]+?))(?:\\s+?)(/?>)",
                                                                     RegexOptions.IgnoreCase);

        private static readonly Regex TagQuotePattern = new Regex("\\s*=\\s*([\"'])([a-z0-9-_]+?)\\1(?=[^<]*?>)",
                                                                  RegexOptions.IgnoreCase);

        private static readonly Regex PrePattern = new Regex("(<pre\b[^>]*?>)((?:(?!</pre>)<[^<]*)*)(</pre>)", RegexOptions.IgnoreCase);

        private static readonly Regex TaPattern = new Regex("(<textarea\b[^>]*?>)((?:(?!</textarea>)<[^<]*)*)(</textarea>)",
                                                            RegexOptions.IgnoreCase);


        private static readonly Regex ScriptPattern
 =
            new Regex("(<script\b[^>]*?>)((?:(?!</script>)<[^<]*)*)(</script>)", RegexOptions.IgnoreCase);


        private static readonly Regex StylePattern = new Regex("(<style[^>]*?>)(.*?)(</style>)", RegexOptions.IgnoreCase);

        private static readonly Regex TagPropertyPattern = new Regex("(\\s\\w+)\\s=\\s(?=[^<]*?>)",
                                                                     RegexOptions.IgnoreCase);

        private static readonly Regex CdataPattern = new Regex("\\s*<!\\[CDATA\\[(.*?)\\]\\]>\\s*",
                                                               RegexOptions.IgnoreCase);

        //unmasked: \son[a-z]+\s*=\s*"[^"\\\r\n]*(?:\\.[^"\\\r\n]*)*"
        private static readonly Regex EventPattern1 =
            new Regex("(\\son[a-z]+\\s*=\\s*\")([^\"\\\\\\r\\n]*(?:\\\\.[^\"\\\\\\r\\n]*)*)(\")",
                      RegexOptions.IgnoreCase);

        private static readonly Regex EventPattern2 =
            new Regex("(\\son[a-z]+\\s*=\\s*')([^'\\\\\\r\\n]*(?:\\\\.[^'\\\\\\r\\n]*)*)(')", RegexOptions.IgnoreCase);

        private static readonly Regex TempCondCommentPattern = new Regex("%%%COMPRESS~COND~(\\d+?)%%%",
                                                                         RegexOptions.IgnoreCase);

        private static readonly Regex TempPrePattern = new Regex("%%%COMPRESS~PRE~(\\d+?)%%%", RegexOptions.IgnoreCase);

        private static readonly Regex TempTextAreaPattern = new Regex("%%%COMPRESS~TEXTAREA~(\\d+?)%%%",
                                                                      RegexOptions.IgnoreCase);

        private static readonly Regex TempScriptPattern = new Regex("%%%COMPRESS~SCRIPT~(\\d+?)%%%",
                                                                    RegexOptions.IgnoreCase);

        private static readonly Regex TempStylePattern = new Regex("%%%COMPRESS~STYLE~(\\d+?)%%%",
                                                                   RegexOptions.IgnoreCase);

        private static readonly Regex TempEventPattern = new Regex("%%%COMPRESS~EVENT~(\\d+?)%%%",
                                                                   RegexOptions.IgnoreCase);

        private bool _compressCss;
        private bool _doCompressJavaScript;
        private bool _enabled = true;
        private List<Regex> _preservePatterns;
        private bool _removeComments = true;
        private bool _removeIntertagSpaces;
        private bool _removeMultiSpaces = true;
        private bool _removeQuotes;
        private int _yuiCssLineBreak = -1;

        //error reporter implementation for YUI compressor
        private Exception _yuiErrorReporter;
        private bool _yuiJsDisableOptimizations;
        private int _yuiJsLineBreak = -1;
        private bool _yuiJsNoMunge;
        private bool _yuiJsPreserveAllSemiColons;

        #region Members

        /// <summary>
        /// Gets a new HTML Compressor
        /// </summary>
        public static Compression Compressor
        {
            get { return new Compression(); }
        }

        /// <summary>
        /// Compresses given HTML source and returns compressed result
        /// </summary>
        /// <param name="html">HTML source to compress</param>
        /// <returns>Compressed HTML</returns>
        public string Compress(string html)
        {
            if (!_enabled || string.IsNullOrEmpty(html))
            {
                return html;
            }

            //preserved block containers

            var condCommentBlocks = new ArrayList();
            var preBlocks = new ArrayList();
            var taBlocks = new ArrayList();
            var scriptBlocks = new ArrayList();
            var styleBlocks = new ArrayList();
            var eventBlocks = new ArrayList();
            var userBlocks = new ArrayList();

            //preserve blocks
            html = PreserveBlocks(html, preBlocks, taBlocks, scriptBlocks, styleBlocks, eventBlocks, condCommentBlocks,
                                  userBlocks);

            //process pure html
            html = ProcessHtml(html);

            //process preserved blocks
            //ProcessScriptBlocks(scriptBlocks);
            //ProcessStyleBlocks(styleBlocks);

            //put blocks back
            html = ReturnBlocks(html, preBlocks, taBlocks, scriptBlocks, styleBlocks, eventBlocks, condCommentBlocks,
                                userBlocks);

            return html.Trim();
        }

        /// <summary>
        /// Compresses given HTML stream and returns compressed result
        /// </summary>
        /// <param name="stream">HTML source to compress</param>
        /// <returns></returns>
        public Stream Compress(Stream stream)
        {
            return new HtmlCompressorFilter(stream);
        }

        /// <summary>
        /// Compresses given HTML source and returns compressed result
        /// </summary>
        /// <param name="html">HTML source to compress</param>
        /// <returns>Compressed HTML</returns>
        public static string CompressString(string html)
        {
            return Compressor.Compress(html);
        }

        /// <summary>
        /// Automatically Compresses the current Response stream using the default settings
        /// </summary>
        public static void CompressResponse()
        {
            HttpContext.Current.Response.Filter = Compressor.Compress(HttpContext.Current.Response.Filter);
        }

        #endregion

        private static string DoReplacement(string html, Regex pattern, IList block, string replacement, int groupToSaveIndex = 1)
        {
            return pattern.Replace(html, m => string.Format(replacement, block.Add(m.Groups[groupToSaveIndex].Value), m.Groups[groupToSaveIndex].Value));
        }

        private static string DoReInsert(string html, Regex tempPattern, IList block)
        {
            return tempPattern.Replace(html, m => block[int.Parse(m.Groups[1].Value)].ToString());
        }


        private string PreserveBlocks(string html, IList preBlocks, IList taBlocks, IList scriptBlocks,
                                      IList styleBlocks, IList eventBlocks, IList condCommentBlocks, IList userBlocks)
        {
            //preserve user blocks
            if (_preservePatterns != null)
            {
                for (int p = 0; p < _preservePatterns.Count; p++)
                {
                    var userBlock = new ArrayList();
                    html = DoReplacement(html, _preservePatterns[p], userBlock, string.Format(TempUserBlock, p), 0);
                    userBlocks.Add(userBlock);
                }
            }

            //preserve conditional comments
            html = DoReplacement(html, CondCommentPattern, condCommentBlocks, TempCondCommentBlock, 0);

            //preserve inline events
            html = DoReplacement(html, EventPattern1, eventBlocks, "$1" + TempEventBlock + "$3");
            html = DoReplacement(html, EventPattern2, eventBlocks, "$1" + TempEventBlock + "$3");

            //preserve PRE tags
            html = DoReplacement(html, PrePattern, preBlocks, TempPreBlock);

            //preserve SCRIPT tags
            html = DoReplacement(html, ScriptPattern, scriptBlocks, TempScriptBlock);

            //preserve STYLE tags
            html = DoReplacement(html, StylePattern, styleBlocks, "$1" + TempStyleBlock + "$3");

            //preserve TEXTAREA tags
            html = DoReplacement(html, TaPattern, taBlocks, TempTextAreaBlock);

            return html;
        }

        private string ReturnBlocks(string html, IList preBlocks, IList taBlocks, IList scriptBlocks, IList styleBlocks,
                                    IList eventBlocks, IList condCommentBlocks, IList userBlocks)
        {
            //put TEXTAREA blocks back
            html = DoReInsert(html, TempTextAreaPattern, taBlocks);

            //put STYLE blocks back
            html = DoReInsert(html, TempStylePattern, styleBlocks);

            //put SCRIPT blocks back
            html = DoReInsert(html, TempScriptPattern, scriptBlocks);

            //put PRE blocks back
            html = DoReInsert(html, TempPrePattern, preBlocks);

            //put event blocks back
            html = DoReInsert(html, TempEventPattern, eventBlocks);

            //put conditional comments back
            html = DoReInsert(html, TempCondCommentPattern, condCommentBlocks);

            //put user blocks back
            if (_preservePatterns != null)
            {
                for (int p = _preservePatterns.Count - 1; p >= 0; p--)
                {
                    var tempUserPattern = new Regex("%%%COMPRESS~USER" + p + "~(\\d+?)%%%");
                    html = DoReInsert(html, tempUserPattern, userBlocks);
                }
            }

            return html;
        }

        private string ProcessHtml(string html)
        {
            //remove comments
            if (_removeComments)
            {
                html = CommentPattern.Replace(html, string.Empty);
            }

            //remove inter-tag spaces
            if (_removeIntertagSpaces)
            {
                html = IntertagPattern.Replace(html, "><");
            }

            //remove multi whitespace characters
            if (_removeMultiSpaces)
            {
                html = MultispacePattern.Replace(html, " ");
            }

            //remove quotes from tag attributes
            if (_removeQuotes)
            {
                html = TagQuotePattern.Replace(html, "=$2");
            }

            //remove spaces around equal sign inside tags
            html = TagPropertyPattern.Replace(html, "$1=");

            //remove ending spaces inside tags
            html = TagEndSpacePattern.Replace(html, "$1$2");

            return html;
        }

        //private void ProcessScriptBlocks(IList scriptBlocks)
        //{
        //    if (!_doCompressJavaScript) return;
        //    for (int i = 0; i < scriptBlocks.Count; i++)
        //    {
        //        string compressed = CompressJavaScript((string) scriptBlocks[i]);
        //        scriptBlocks.RemoveAt(i);
        //        scriptBlocks.Insert(i, compressed);
        //    }
        //}

        //private void ProcessStyleBlocks(IList styleBlocks)
        //{
        //    if (!_compressCss) return;
        //    for (int i = 0; i < styleBlocks.Count; i++)
        //    {
        //        string compressed = CompressCssStyles((string) styleBlocks[i]);
        //        styleBlocks.RemoveAt(i);
        //        styleBlocks.Insert(i, compressed);
        //    }
        //}

        //private static string CompressJavaScript(string source)
        //{
        //    return source;

        //    //detect CDATA wrapper
        //    bool cdataWrapper = false;
        //    MatchCollection matcher = CdataPattern.Matches(source);
        //    if (matcher.Count > 0)
        //    {
        //        cdataWrapper = true;
        //        source = matcher[0].Groups[1].Value;
        //    }

        //    //call YUICompressor
        //    var result = new StringWriter();
        //    //JavaScriptCompressor compressor = new JavaScriptCompressor(new StringReader(source), yuiErrorReporter);
        //    //compressor.compress(result, yuiJsLineBreak, !yuiJsNoMunge, false, yuiJsPreserveAllSemiColons, yuiJsDisableOptimizations);

        //    if (cdataWrapper)
        //    {
        //        return "<![CDATA[" + result + "]]>";
        //    }
        //    else
        //    {
        //        return result.ToString();
        //    }
        //}

        //private static string CompressCssStyles(string source)
        //{
        //    return source;

        //    //call YUICompressor
        //    var result = new StringWriter();
        //    //CssCompressor compressor = new CssCompressor(new StringReader(source));
        //    //compressor.compress(result, yuiCssLineBreak);


        //    return result.ToString();
        //}

        ///// <summary>
        /////  Returns true if Javascript Compression is enabled
        ///// </summary>
        ///// <returns>current state of JavaScript compression.</returns>
        //public bool IsCompressJavaScript()
        //{
        //    return _doCompressJavaScript;
        //}

        //**
        // * Enables JavaScript compression within &lt;script> tags using 
        // * <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a> 
        // * if set to <code>true</code>. Default is <code>false</code> for performance reasons.
        // *  
        // * Note: Compressing JavaScript is not recommended if pages are 
        // * compressed dynamically on-the-fly because of performance impact. 
        // * You should consider putting JavaScript into a separate file and
        // * compressing it using standalone YUICompressor for example.
        // * 
        // * @param compressJavaScript set true to enable JavaScript compression. 
        // * Default is false
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // * 
        // */

        //public void SetCompressJavaScript(bool compressJavaScript)
        //{
        //    _doCompressJavaScript = compressJavaScript;
        //}

        //**
        // * Returns <code>true</code> if CSS compression is enabled.
        // * 
        // * @return current state of CSS compression.
        // */

        //public bool IsCompressCss()
        //{
        //    return _compressCss;
        //}

        //**
        // * Enables CSS compression within &lt;style> tags using 
        // * <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a> 
        // * if set to <code>true</code>. Default is <code>false</code> for performance reasons.
        // *  
        // * <p><b>Note:</b> Compressing CSS is not recommended if pages are 
        // * compressed dynamically on-the-fly because of performance impact. 
        // * You should consider putting CSS into a separate file and
        // * compressing it using standalone YUICompressor for example.</p>
        // * 
        // * @param compressCss set <code>true</code> to enable CSS compression. 
        // * Default is <code>false</code>
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // * 
        // */

        //public void SetCompressCss(bool compressCss)
        //{
        //    _compressCss = compressCss;
        //}

        //**
        // * Returns <code>true</code> if Yahoo YUI Compressor
        // * will only minify javascript without obfuscating local symbols. 
        // * This corresponds to <code>--nomunge</code> command line option.  
        // *   
        // * @return <code>nomunge</code> parameter value used for JavaScript compression.
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // */

        //public bool IsYuiJsNoMunge()
        //{
        //    return _yuiJsNoMunge;
        //}

        //**
        // * Tells Yahoo YUI Compressor to only minify javascript without obfuscating 
        // * local symbols. This corresponds to <code>--nomunge</code> command line option. 
        // * This option has effect only if JavaScript compression is enabled. 
        // * Default is <code>false</code>.
        // * 
        // * @param yuiJsNoMunge set <code>true<code> to enable <code>nomunge</code> mode
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // */

        //public void SetYuiJsNoMunge(bool yuiJsNoMunge)
        //{
        //    _yuiJsNoMunge = yuiJsNoMunge;
        //}

        //**
        // * Returns <code>true</code> if Yahoo YUI Compressor
        // * will preserve unnecessary semicolons during JavaScript compression. 
        // * This corresponds to <code>--preserve-semi</code> command line option.
        // *   
        // * @return <code>preserve-semi</code> parameter value used for JavaScript compression.
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // */

        //public bool IsYuiJsPreserveAllSemiColons()
        //{
        //    return _yuiJsPreserveAllSemiColons;
        //}

        //**
        // * Tells Yahoo YUI Compressor to preserve unnecessary semicolons 
        // * during JavaScript compression. This corresponds to 
        // * <code>--preserve-semi</code> command line option. 
        // * This option has effect only if JavaScript compression is enabled.
        // * Default is <code>false</code>.
        // * 
        // * @param yuiJsPreserveAllSemiColons set <code>true<code> to enable <code>preserve-semi</code> mode
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // */

        //public void SetYuiJsPreserveAllSemiColons(bool yuiJsPreserveAllSemiColons)
        //{
        //    _yuiJsPreserveAllSemiColons = yuiJsPreserveAllSemiColons;
        //}

        //**
        // * Returns <code>true</code> if Yahoo YUI Compressor
        // * will disable all the built-in micro optimizations during JavaScript compression. 
        // * This corresponds to <code>--disable-optimizations</code> command line option.
        // *   
        // * @return <code>disable-optimizations</code> parameter value used for JavaScript compression.
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // */

        //public bool IsYuiJsDisableOptimizations()
        //{
        //    return _yuiJsDisableOptimizations;
        //}

        //**
        // * Tells Yahoo YUI Compressor to disable all the built-in micro optimizations
        // * during JavaScript compression. This corresponds to 
        // * <code>--disable-optimizations</code> command line option. 
        // * This option has effect only if JavaScript compression is enabled.
        // * Default is <code>false</code>.
        // * 
        // * @param yuiJsDisableOptimizations set <code>true<code> to enable 
        // * <code>disable-optimizations</code> mode
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // */

        //public void SetYuiJsDisableOptimizations(bool yuiJsDisableOptimizations)
        //{
        //    _yuiJsDisableOptimizations = yuiJsDisableOptimizations;
        //}

        //**
        // * Returns number of symbols per line Yahoo YUI Compressor
        // * will use during JavaScript compression. 
        // * This corresponds to <code>--line-break</code> command line option.
        // *   
        // * @return <code>line-break</code> parameter value used for JavaScript compression.
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // */

        //public int GetYuiJsLineBreak()
        //{
        //    return _yuiJsLineBreak;
        //}

        //**
        // * Tells Yahoo YUI Compressor to break lines after the specified number of symbols 
        // * during JavaScript compression. This corresponds to 
        // * <code>--line-break</code> command line option. 
        // * This option has effect only if JavaScript compression is enabled.
        // * Default is <code>-1</code> to disable line breaks.
        // * 
        // * @param yuiJsLineBreak set number of symbols per line
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // */

        //public void SetYuiJsLineBreak(int yuiJsLineBreak)
        //{
        //    _yuiJsLineBreak = yuiJsLineBreak;
        //}

        //**
        // * Returns number of symbols per line Yahoo YUI Compressor
        // * will use during CSS compression. 
        // * This corresponds to <code>--line-break</code> command line option.
        // *   
        // * @return <code>line-break</code> parameter value used for CSS compression.
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // */

        //public int GetYuiCssLineBreak()
        //{
        //    return _yuiCssLineBreak;
        //}

        //**
        // * Tells Yahoo YUI Compressor to break lines after the specified number of symbols 
        // * during CSS compression. This corresponds to 
        // * <code>--line-break</code> command line option. 
        // * This option has effect only if CSS compression is enabled.
        // * Default is <code>-1</code> to disable line breaks.
        // * 
        // * @param yuiCssLineBreak set number of symbols per line
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // */

        //public void SetYuiCssLineBreak(int yuiCssLineBreak)
        //{
        //    _yuiCssLineBreak = yuiCssLineBreak;
        //}


        /// <summary>
        /// Returns true if all unneccasary quotes will be removed from tag attributes
        /// </summary>
        public bool IsRemoveQuotes()
        {
            return _removeQuotes;
        }


        /// <summary>
        /// If set to true all unnecessary quotes will be removed  
        /// from tag attributes. Default is false.
        /// Note: Even though quotes are removed only when it is safe to do so, 
        /// it still might break strict HTML validation. Turn this option on only if 
        /// a page validation is not very important or to squeeze the most out of the compression.
        /// This option has no performance impact. 
        /// </summary>
        /// <param name="removeQuotes">removeQuotes set true to remove unnecessary quotes from tag attributes</param>
        public void SetRemoveQuotes(bool removeQuotes)
        {
            _removeQuotes = removeQuotes;
        }

        /// <summary>
        /// Returns <code>true</code> if compression is enabled.  
        /// </summary>
        /// <returns><code>true</code> if compression is enabled.</returns>
        public bool IsEnabled()
        {
            return _enabled;
        }

        /**
         * If set to <code>false</code> all compression will be bypassed. Might be useful for testing purposes. 
         * Default is <code>true</code>.
         * 
         * @param enabled set <code>false</code> to bypass all compression
         */

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        /**
         * Returns <code>true</code> if all HTML comments will be removed.
         * 
         * @return <code>true</code> if all HTML comments will be removed
         */

        public bool IsRemoveComments()
        {
            return _removeComments;
        }

        /**
         * If set to <code>true</code> all HTML comments will be removed.   
         * Default is <code>true</code>.
         * 
         * @param removeComments set <code>true</code> to remove all HTML comments
         */

        public void SetRemoveComments(bool removeComments)
        {
            _removeComments = removeComments;
        }

        /**
         * Returns <code>true</code> if all multiple whitespace characters will be replaced with single spaces.
         * 
         * @return <code>true</code> if all multiple whitespace characters will be replaced with single spaces.
         */

        public bool IsRemoveMultiSpaces()
        {
            return _removeMultiSpaces;
        }

        /**
         * If set to <code>true</code> all multiple whitespace characters will be replaced with single spaces.
         * Default is <code>true</code>.
         * 
         * @param removeMultiSpaces set <code>true</code> to replace all multiple whitespace characters 
         * will single spaces.
         */

        public void SetRemoveMultiSpaces(bool removeMultiSpaces)
        {
            _removeMultiSpaces = removeMultiSpaces;
        }

        /**
         * Returns <code>true</code> if all inter-tag whitespace characters will be removed.
         * 
         * @return <code>true</code> if all inter-tag whitespace characters will be removed.
         */

        public bool IsRemoveIntertagSpaces()
        {
            return _removeIntertagSpaces;
        }

        /**
         * If set to <code>true</code> all inter-tag whitespace characters will be removed.
         * Default is <code>false</code>.
         * 
         * <p><b>Note:</b> It is fairly safe to turn this option on unless you 
         * rely on spaces for page formatting. Even if you do, you can always preserve 
         * required spaces with <code>&amp;nbsp;</code>. This option has no performance impact.    
         * 
         * @param removeIntertagSpaces set <code>true</code> to remove all inter-tag whitespace characters
         */

        public void SetRemoveIntertagSpaces(bool removeIntertagSpaces)
        {
            _removeIntertagSpaces = removeIntertagSpaces;
        }

        /**
         * Returns a list of Patterns defining custom preserving block rules  
         * 
         * @return list of <code>Pattern</code> objects defining rules for preserving block rules
         */

        public List<Regex> GetPreservePatterns()
        {
            return _preservePatterns;
        }

        /**
         * This method allows setting custom block preservation rules defined by regular 
         * expression patterns. Blocks that match provided patterns will be skipped during HTML compression. 
         * 
         * <p>Custom preservation rules have higher priority than default rules.
         * Priority between custom rules are defined by their position in a list 
         * (beginning of a list has higher priority).
         * 
         * @param preservePatterns List of <code>Pattern</code> objects that will be 
         * used to skip matched blocks during compression  
         */

        public void SetPreservePatterns(List<Regex> preservePatterns)
        {
            _preservePatterns = preservePatterns;
        }

        ///**
        // * Returns <code>ErrorReporter</code> used by YUI Compressor to log error messages 
        // * during JavasSript compression 
        // * 
        // * @return <code>ErrorReporter</code> used by YUI Compressor to log error messages 
        // * during JavasSript compression
        // * 
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // * @see <a href="http://www.mozilla.org/rhino/apidocs/org/mozilla/javascript/ErrorReporter.html">Error Reporter Interface</a>
        // */

        //public Exception GetYuiErrorReporter()
        //{
        //    return _yuiErrorReporter;
        //}

        ///**
        // * Sets <code>ErrorReporter</code> that YUI Compressor will use for reporting errors during 
        // * JavaScript compression. If no <code>ErrorReporter</code> was provided a <code>NullPointerException</code> 
        // * will be throuwn in case of an error during the compression.
        // * 
        // * <p>For simple error reporting that uses <code>System.err</code> stream 
        // * {@link DefaultErrorReporter} can be used. 
        // * 
        // * @param yuiErrorReporter <code>ErrorReporter<code> that will be used by YUI Compressor
        // * 
        // * @see DefaultErrorReporter
        // * @see <a href="http://developer.yahoo.com/yui/compressor/">Yahoo YUI Compressor</a>
        // * @see <a href="http://www.mozilla.org/rhino/apidocs/org/mozilla/javascript/ErrorReporter.html">ErrorReporter Interface</a>
        // */

        //public void SetYuiErrorReporter(Exception yuiErrorReporter)
        //{
        //    _yuiErrorReporter = yuiErrorReporter;
        //}
    }
}