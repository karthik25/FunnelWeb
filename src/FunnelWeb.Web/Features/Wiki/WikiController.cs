﻿using System;
using System.Linq;
using System.Web.Mvc;
using FunnelWeb.Web.Application.Filters;
using FunnelWeb.Web.Application.Mvc;
using FunnelWeb.Web.Application.Spam;
using FunnelWeb.Web.Features.Wiki.Views;
using FunnelWeb.Web.Model;
using FunnelWeb.Web.Model.Repositories;
using FunnelWeb.Web.Model.Strings;

namespace FunnelWeb.Web.Features.Wiki
{
    [Transactional]
    [HandleError]
    public partial class WikiController : Controller
    {
        private const int ItemsPerPage = 30;
        public IEntryRepository EntryRepository { get; set; }
        public IFeedRepository FeedRepository { get; set; }
        public ISpamChecker SpamChecker { get; set; }

        public virtual ActionResult Recent(int pageNumber)
        {
            var feed = FeedRepository.GetFeeds().OrderBy(f => f.Id).First().Name;

            var entries = FeedRepository.GetFeed(feed, pageNumber * ItemsPerPage, ItemsPerPage);
            var totalItems = FeedRepository.GetFeedCount(feed);
            ViewData.Model = new RecentModel(entries, pageNumber, (int)((decimal)totalItems / ItemsPerPage + 1));
            return View();
        }

        public virtual ActionResult Search([Bind(Prefix = "q")] string searchText)
        {
            var results = EntryRepository.Search(searchText);
            ViewData.Model = new NotFoundModel(searchText, false, results);
            return View("NotFound");
        }

        public virtual ActionResult NotFound(string searchText)
        {
            var redirect = EntryRepository.GetClosestRedirect(HttpContext.Request.Url.AbsolutePath);
            if (redirect != null)
            {
                return redirect.To.StartsWith("http") 
                    ? Redirect(redirect.To) 
                    : Redirect("~/" + redirect.To);
            }

            var results = EntryRepository.Search(searchText);
            ViewData.Model = new NotFoundModel(searchText, true, results);
            return View("NotFound");
        }

        public virtual ActionResult Page(PageName page, int revision)
        {
            var entry = EntryRepository.GetEntry(page, revision);
            if (entry == null)
            {
                if (HttpContext.User.Identity.IsAuthenticated)
                {
                    return RedirectToAction(FunnelWebMvc.Wiki.Actions.Edit(page));
                }
                return NotFound(page);
            }

            ViewData.Model = new PageModel(page, entry, revision > 0);
            return View();
        }
        
        [Authorize]
        public virtual ActionResult New()
        {
            var feeds = FeedRepository.GetFeeds();
            var model = new EditModel("", true, feeds);
            model.Title = "Enter a title";
            model.AllowComments = true;
            model.MetaTitle = "Enter a meta title";
            return View("Edit", model);
        }

        [Authorize]
        public virtual ActionResult Unpublished()
        {
            var allPosts = EntryRepository.GetUnpublished();
            ViewData.Model = new RecentModel(allPosts, 1, 1);
            return View();
        }

        [Authorize]
        public virtual ActionResult Edit(PageName page)
        {
            var entry = EntryRepository.GetEntry(page) ?? new Entry() { Title = page, MetaTitle = page, Name = page};
            var feeds = FeedRepository.GetFeeds();
            var model = new EditModel(page, entry.Id == 0, feeds);
            model.AllowComments = entry.IsDiscussionEnabled;
            model.ChangeSummary = entry.Id == 0 ? "Initial create" : "";
            model.Content = entry.LatestRevision.Body;
            model.Keywords = entry.MetaKeywords;
            model.MetaDescription = entry.MetaDescription;
            model.MetaTitle = entry.MetaTitle;
            model.PublishDate = entry.Published;
            model.Sidebar = entry.Summary;
            model.Title = entry.Title;
            return View(model);
        }

        [HttpPost]
        [Authorize]
        public virtual ActionResult Edit(EditModel model)
        {
            var feeds = FeedRepository.GetFeeds();
            model.Feeds = feeds;
                
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var entry = EntryRepository.GetEntry(model.Page) ?? new Entry();
            entry.Name = model.Page;
            entry.Title = model.Title ?? string.Empty;
            entry.Summary = model.Sidebar ?? string.Empty;
            entry.MetaTitle = model.MetaTitle ?? string.Empty;
            entry.IsDiscussionEnabled = model.AllowComments;
            entry.MetaDescription = model.MetaDescription ?? string.Empty;
            entry.MetaKeywords = model.Keywords ?? string.Empty;
            entry.Published = (model.PublishDate ?? DateTime.Now).ToUniversalTime();

            var revision = entry.Revise();
            revision.Body = model.Content;
            revision.Reason = model.ChangeSummary;

            EntryRepository.Save(entry);

            foreach (var feed in FeedRepository.GetFeeds())
            {
                if (model.FeedIds == null || model.FeedIds.Contains(feed.Id) == false) 
                    continue;

                feed.Publish(entry);
                FeedRepository.Save(feed);
            }

            return RedirectToAction("Page", new { page = model.Page });
        }

        [HttpPost]
        public virtual ActionResult Page(PageName page, PageModel model)
        {
            var entry = EntryRepository.GetEntry(page);
            if (entry == null) 
                return RedirectToAction("Recent");

            if (!ModelState.IsValid)
            {
                model.Entry = entry;
                model.IsPriorVersion = false;
                model.Page = page;
                return View("Page", model)
                    .AndFlash("Your comment was not posted - please check the validation errors below.");
            }

            var comment = entry.Comment();
            comment.AuthorCompany = string.Empty;
            comment.AuthorEmail = model.CommenterEmail ?? string.Empty;
            comment.AuthorName = model.CommenterName ?? string.Empty;
            comment.AuthorUrl = model.CommenterBlog ?? string.Empty;
            comment.Body = model.Comments;

            try
            {
                SpamChecker.Verify(comment);
            }
            catch (Exception ex)
            {
                HttpContext.Trace.Warn("Akismet is offline, comment cannot be validated: " + ex);
            }

            EntryRepository.Save(entry);

            return RedirectToAction("Page", new {page = page})
                .AndFlash("Thanks, your comment has been posted.");
        }

        public virtual ActionResult Revisions(PageName page)
        {
            var entry = EntryRepository.GetEntry(page);
            if (entry == null)
            {
                return RedirectToAction("Edit", new { page = page });
            }

            ViewData.Model = new RevisionsModel(page, entry);
            return View();
        }

        public virtual ActionResult SiteMap()
        {
            var allPosts = EntryRepository.GetEntries().OrderBy(x => x.Published).ToList();
            ViewData.Model = new SiteMapModel(allPosts);
            return View();
        }
    }
}