// <copyright file="ArticleTypeClassifierTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Vector.NNTP.Articles.Classification;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="ArticleTypeClassifier"/>.
/// </summary>
[TestFixture]
public sealed class ArticleTypeClassifierTests
{
    /// <summary>
    /// Verifies plain text articles remain <see cref="ArticleTypeFlags.Default"/>.
    /// </summary>
    [Test]
    public void Classify_PlainText_ReturnsDefault()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nSubject: hi\r\n\r\nHello world.\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags, Is.EqualTo(ArticleTypeFlags.Default));
    }

    /// <summary>
    /// Verifies yEnc begin lines set <see cref="ArticleTypeFlags.YEnc"/> and <see cref="ArticleTypeFlags.Binary"/>.
    /// </summary>
    [Test]
    public void Classify_YEncBegin_SetsYEncAndBinary()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\n\r\n=ybegin line=128 size=10 name=test.dat\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.YEnc), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Binary), Is.True);
    }

    /// <summary>
    /// Verifies <c>Content-Type: application/octet-stream</c> does not prevent yEnc detection in the body.
    /// </summary>
    [Test]
    public void Classify_OctetStreamHeaderWithYEncBody_SetsYEnc()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\n" +
            "Content-Type: application/octet-stream;\r\n" +
            "    name=\"part215.rar\"\r\n" +
            "\r\n" +
            "=ybegin line=128 size=217055232 part=114 total=114\r\n" +
            "=ypart begin=216960001 end=217055232\r\n" +
            "payload\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.YEnc), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Binary), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Mime), Is.True);
    }

    /// <summary>
    /// Verifies multipart yEnc with octet-stream MIME wrapper sets <see cref="ArticleTypeFlags.Partial"/>.
    /// </summary>
    [Test]
    public void Classify_OctetStreamHeaderWithYEncPart_SetsYEncAndPartial()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nContent-Type: application/octet-stream\r\n\r\n" +
            "=ybegin part=114 total=114 line=128 size=10 name=test.dat\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.YEnc), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Partial), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Binary), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Mime), Is.True);
    }

    /// <summary>
    /// Verifies base64 transfer encoding in headers still terminates body scan without reading the body.
    /// </summary>
    [Test]
    public void Classify_Base64TransferEncoding_StillStopsEarly()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nContent-transfer-encoding: base64\r\n\r\n" +
            "dGVzdCBib2R5IGRhdGE=\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.Base64), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Binary), Is.True);
    }

    /// <summary>
    /// Verifies MIME headers set <see cref="ArticleTypeFlags.Mime"/>.
    /// </summary>
    [Test]
    public void Classify_MimeHeaders_SetsMime()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nContent-Type: text/plain\r\nMime-Version: 1.0\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.Mime), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Text), Is.True);
    }

    /// <summary>
    /// Verifies classification stops scanning body content after <see cref="ArticleTypeClassifier.MaxClassificationBytes"/>.
    /// </summary>
    [Test]
    public void Classify_LargeBody_StopsWithinBodyCap()
    {
        var builder = new StringBuilder();
        builder.Append("Path: misc.test\r\n\r\n");
        builder.Append('A', ArticleTypeClassifier.MaxClassificationBytes + 8_192);
        builder.Append("\r\n=ybegin line=128 size=10 name=late.dat\r\n");
        byte[] article = Encoding.ASCII.GetBytes(builder.ToString());

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.YEnc), Is.False);
    }

    /// <summary>
    /// Verifies large headers do not consume the body classification budget.
    /// </summary>
    [Test]
    public void Classify_LargeHeaders_StillClassifiesBodyWithinBudget()
    {
        const int headerPayloadBytes = 50 * 1024;
        const int bodyPaddingBytes = 20 * 1024;
        var builder = new StringBuilder();
        builder.Append("Path: misc.test\r\nX-Large: ");
        builder.Append('H', headerPayloadBytes);
        builder.Append("\r\n\r\n");
        builder.Append('A', bodyPaddingBytes);
        builder.Append("\r\n=ybegin line=128 size=10 name=late.dat\r\n");
        byte[] article = Encoding.ASCII.GetBytes(builder.ToString());

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.YEnc), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Binary), Is.True);
    }

    /// <summary>
    /// Verifies header-only flags are still detected after an earlier header sets <see cref="ArticleTypeFlags.Binary"/>.
    /// </summary>
    [Test]
    public void Classify_BinaryHeaderBeforeApproved_StillSetsApproved()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nContent-Type: application/octet-stream\r\nApproved: moderator@example.com\r\n\r\nbody\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.Binary), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Approved), Is.True);
    }

    /// <summary>
    /// Verifies <see cref="ArticleTypeFlags.Approved"/> detection.
    /// </summary>
    [Test]
    public void Classify_ApprovedHeader_SetsApproved()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nApproved: mod@example.com\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.Approved), Is.True);
    }

    /// <summary>
    /// Verifies <see cref="ArticleTypeFlags.Supersedes"/> detection.
    /// </summary>
    [Test]
    public void Classify_SupersedesHeader_SetsSupersedes()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nSupersedes: <old@example.com>\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.Supersedes), Is.True);
    }

    /// <summary>
    /// Verifies PGP signature armor and multipart/signed set <see cref="ArticleTypeFlags.PgpSigned"/>.
    /// </summary>
    [Test]
    public void Classify_PgpSignedMultipartAndArmor_SetsPgpSigned()
    {
        byte[] multipart = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nContent-Type: multipart/signed; protocol=\"application/pgp-signature\"\r\n\r\n");
        byte[] armor = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\n\r\n-----BEGIN PGP SIGNATURE-----\r\nVersion: GnuPG\r\n\r\n");

        ArticleTypeFlags multipartFlags = ArticleTypeClassifier.Classify(multipart);
        ArticleTypeFlags armorFlags = ArticleTypeClassifier.Classify(armor);

        Assert.That(multipartFlags.HasFlag(ArticleTypeFlags.PgpSigned), Is.True);
        Assert.That(armorFlags.HasFlag(ArticleTypeFlags.PgpSigned), Is.True);
    }

    /// <summary>
    /// Verifies S/MIME pkcs7 content types set <see cref="ArticleTypeFlags.Smime"/>.
    /// </summary>
    [Test]
    public void Classify_SmimeContentType_SetsSmime()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nContent-Type: application/pkcs7-mime\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.Smime), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Mime), Is.True);
    }

    /// <summary>
    /// Verifies multipart MIME subtypes set subtype flags and generic <see cref="ArticleTypeFlags.Multipart"/>.
    /// </summary>
    /// <param name="contentType">Content-Type header value under test.</param>
    /// <param name="subtype">Expected multipart subtype flag.</param>
    [TestCase("Content-Type: multipart/mixed", ArticleTypeFlags.MultipartMixed)]
    [TestCase("Content-Type: multipart/alternative", ArticleTypeFlags.MultipartAlternative)]
    [TestCase("Content-Type: multipart/related", ArticleTypeFlags.MultipartRelated)]
    [TestCase("Content-Type: multipart/signed", ArticleTypeFlags.MultipartSigned)]
    public void Classify_MultipartSubtypes_SetsSubtypeAndMultipart(string contentType, ArticleTypeFlags subtype)
    {
        byte[] article = Encoding.ASCII.GetBytes($"Path: misc.test\r\n{contentType}\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(subtype), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Multipart), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Mime), Is.True);
    }

    /// <summary>
    /// Verifies MIME taxonomy flags for archive, image, video, audio, and text.
    /// </summary>
    /// <param name="contentType">Content-Type header value under test.</param>
    /// <param name="family">Expected content family flag.</param>
    /// <param name="expectsBinary">Whether <see cref="ArticleTypeFlags.Binary"/> should also be set.</param>
    [TestCase("Content-Type: application/zip", ArticleTypeFlags.Archive, true)]
    [TestCase("Content-Type: image/jpeg", ArticleTypeFlags.Image, true)]
    [TestCase("Content-Type: video/mp4", ArticleTypeFlags.Video, true)]
    [TestCase("Content-Type: audio/mpeg", ArticleTypeFlags.Audio, true)]
    [TestCase("Content-Type: text/plain", ArticleTypeFlags.Text, false)]
    public void Classify_ContentTaxonomy_SetsFamilyFlag(string contentType, ArticleTypeFlags family, bool expectsBinary)
    {
        byte[] article = Encoding.ASCII.GetBytes($"Path: misc.test\r\n{contentType}\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(family), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Mime), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Binary), Is.EqualTo(expectsBinary));
    }

    /// <summary>
    /// Verifies NZB poster hints set <see cref="ArticleTypeFlags.NzbGenerated"/>.
    /// </summary>
    /// <param name="headerLine">Header line containing a known automated poster token.</param>
    [TestCase("X-Newsposter: Nyuu 2.0")]
    [TestCase("User-Agent: ngPost/1.0")]
    [TestCase("User-Agent: YEnc-PowerPost 11.0")]
    public void Classify_NzbPosterHints_SetsNzbGenerated(string headerLine)
    {
        byte[] article = Encoding.ASCII.GetBytes($"Path: misc.test\r\n{headerLine}\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.NzbGenerated), Is.True);
    }

    /// <summary>
    /// Verifies ten or more newsgroups set <see cref="ArticleTypeFlags.MassCrosspost"/>.
    /// </summary>
    [Test]
    public void Classify_TenNewsgroups_SetsMassCrosspost()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nNewsgroups: a,b,c,d,e,f,g,h,i,j\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.MassCrosspost), Is.True);
    }

    /// <summary>
    /// Verifies fewer than ten newsgroups do not set <see cref="ArticleTypeFlags.MassCrosspost"/>.
    /// </summary>
    [Test]
    public void Classify_NineNewsgroups_DoesNotSetMassCrosspost()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nNewsgroups: a,b,c,d,e,f,g,h,i\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.MassCrosspost), Is.False);
    }

    /// <summary>
    /// Verifies differing <c>Followup-To:</c> and <c>Newsgroups:</c> set <see cref="ArticleTypeFlags.FollowupRedirect"/>.
    /// </summary>
    [Test]
    public void Classify_FollowupRedirect_SetsFlag()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nNewsgroups: misc.test\r\nFollowup-To: misc.admin\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.FollowupRedirect), Is.True);
    }

    /// <summary>
    /// Verifies identical followup and newsgroups do not set <see cref="ArticleTypeFlags.FollowupRedirect"/>.
    /// </summary>
    [Test]
    public void Classify_MatchingFollowup_DoesNotSetFollowupRedirect()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nNewsgroups: misc.test\r\nFollowup-To: misc.test\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.FollowupRedirect), Is.False);
    }

    /// <summary>
    /// Verifies signed control composite sets <see cref="ArticleTypeFlags.SignedControl"/>.
    /// </summary>
    [Test]
    public void Classify_SignedControl_SetsSignedControl()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nControl: newgroup misc.test.new\r\nApproved: mod@example.com\r\nContent-Type: multipart/signed\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.Control), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Approved), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.PgpSigned), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.SignedControl), Is.True);
    }

    /// <summary>
    /// Verifies control plus approved without signature does not set <see cref="ArticleTypeFlags.SignedControl"/>.
    /// </summary>
    [Test]
    public void Classify_UnsignedControl_DoesNotSetSignedControl()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: misc.test\r\nControl: newgroup misc.test.new\r\nApproved: mod@example.com\r\n\r\n");

        ArticleTypeFlags flags = ArticleTypeClassifier.Classify(article);

        Assert.That(flags.HasFlag(ArticleTypeFlags.Control), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.Approved), Is.True);
        Assert.That(flags.HasFlag(ArticleTypeFlags.SignedControl), Is.False);
    }
}
