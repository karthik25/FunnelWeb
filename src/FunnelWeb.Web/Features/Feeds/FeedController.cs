using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Web.Mvc;
using FunnelWeb.Web.Application.Filters;
using FunnelWeb.Web.Application.Mvc.ActionResults;
using FunnelWeb.Web.Application.Settings;
using FunnelWeb.Web.Application.Views;
using FunnelWeb.Web.Model.Repositories;
using FunnelWeb.Web.Model.Strings;

namespace FunnelWeb.Web.Features.Feeds
{
    [Transactional]
    public partial class FeedController : Controller
    {
        public IFeedRepository FeedRepository { get; set; }
        public IMarkdownProvider Markdown { get; set; }
        public ISettingsProvider Settings { get; set; }

        private FeedResult FeedResult(IEnumerable<SyndicationItem> items)
        {
            var settings = Settings.GetSettings();

            return new FeedResult(
                new Atom10FeedFormatter(
                    new SyndicationFeed(settings.SiteTitle, settings.SearchDescription, new Uri(Url.ActionAbsolute(FunnelWebMvc.Wiki.Recent())), items)
                    {
                        Id = Request.Url.ToString(),
                        Links = 
                        { 
                            new SyndicationLink(Request.Url) 
                            { 
                                RelationshipType = "self" 
                            }
                        }
                    }))
                {
                    ContentType = "application/atom+xml"
                };
        }

        public virtual ActionResult Feed(PageName feedName)
        {
            var settings = Settings.GetSettings();
            if (String.IsNullOrWhiteSpace(feedName))
            {
                feedName = FeedRepository.GetFeeds().OrderBy(f => f.Id).First().Name;
            }
            var entries = FeedRepository.GetFeed(feedName, 0, 20);

            var items = 
                from e in entries
                let itemUri = new Uri(Request.Url, Url.Action("Page", "Wiki", new {page = e.Name}))
                select new
                {
                    Item = new SyndicationItem
                    {
                        Id = itemUri.ToString(),
                        Title = TextSyndicationContent.CreatePlaintextContent(e.Title),
                        Summary = TextSyndicationContent.CreateHtmlContent(
                            Markdown.Render(e.LatestRevision.Body) +
                            String.Format("<img src=\"{0}\" />", itemUri + "/via-feed")),
                        LastUpdatedTime = e.LatestRevision.Revised,
                        Links =
                            {
                                new SyndicationLink(itemUri)
                            },
                        Authors =
                            {
                                new SyndicationPerson {Name = settings.Author}
                            },
                    },
                    Keywords = e.MetaKeywords.Split(',')
                };

            foreach (var item in items.ToList())
            {
                foreach (var keyword in item.Keywords)
                {
                    item.Item.Categories.Add(new SyndicationCategory(keyword));
                }
            }

            return FeedResult(items.Select(i => i.Item));
        }

        public virtual ActionResult CommentFeed()
        {
            var comments = FeedRepository.GetCommentFeed(0, 20);

            var items = 
                from e in comments
                let itemUri = new Uri(Request.Url, Url.Action("Page", "Wiki", new { page = e.Entry.Name, comment = e.Id }))
                select new SyndicationItem
                {
                    Id = itemUri.ToString(),
                    Title = SyndicationContent.CreatePlaintextContent(e.AuthorName + " on " + e.Entry.Title),
                    Summary = SyndicationContent.CreateHtmlContent(Markdown.Render(e.Body, true)),
                    LastUpdatedTime = e.Posted,
                    Links = 
                    {
                        new SyndicationLink(itemUri) 
                    },
                    Authors = 
                    {
                        new SyndicationPerson { Name = e.AuthorName, Uri = e.AuthorUrl } 
                    },
                };

            return FeedResult(items);
        }
    }
}