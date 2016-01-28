﻿using System;
using System.Collections.Generic;
using System.Linq;
using Orchard.ContentManagement;
using Orchard.ContentManagement.MetaData;
using Orchard.Contents.ViewModels;
using Microsoft.AspNet.Mvc;
using Orchard.DisplayManagement.ModelBinding;
using Microsoft.Extensions.Logging;
using Orchard.Navigation;
using Orchard.Core.Settings.Services;
using System.Threading.Tasks;
using YesSql.Core.Services;
using Orchard.ContentManagement.Records;
using Orchard.DisplayManagement;
using Orchard.ContentManagement.Display;
using YesSql.Core.Indexes;
using Orchard.ContentManagement.MetaData.Models;
using Orchard.ContentManagement.MetaData.Settings;
using Orchard.Mvc;
using Microsoft.AspNet.Routing;

namespace Orchard.Contents.Controllers
{
    public class AdminController : Controller, IUpdateModel
    {
        private readonly IContentManager _contentManager;
        private readonly IContentDefinitionManager _contentDefinitionManager;
        private readonly ISiteService _siteService;
        private readonly ISession _session;
        private readonly IContentItemDisplayManager _contentItemDisplayManager;

        public AdminController(
            IContentManager contentManager,
            IContentItemDisplayManager contentItemDisplayManager,
            IContentDefinitionManager contentDefinitionManager,
            ISiteService siteService,
            ISession session,
            IShapeFactory shapeFactory,
            ILogger<AdminController> logger
            )
        {
            _contentItemDisplayManager = contentItemDisplayManager;
            _session = session;
            _siteService = siteService;
            _contentManager = contentManager;
            _contentDefinitionManager = contentDefinitionManager;

            New = shapeFactory;
            Logger = logger;
        }

        public dynamic New { get; set; }

        public ILogger Logger { get; set; }

        public async Task<IActionResult> List(ListContentsViewModel model, PagerParameters pagerParameters)
        {
            var siteSettings = await _siteService.GetSiteSettingsAsync();
            Pager pager = new Pager(pagerParameters, siteSettings.PageSize);

            var versionOptions = VersionOptions.Latest;
            switch (model.Options.ContentsStatus)
            {
                case ContentsStatus.Published:
                    versionOptions = VersionOptions.Published;
                    break;
                case ContentsStatus.Draft:
                    versionOptions = VersionOptions.Draft;
                    break;
                case ContentsStatus.AllVersions:
                    versionOptions = VersionOptions.AllVersions;
                    break;
                default:
                    versionOptions = VersionOptions.Latest;
                    break;
            }

            // TODO: Filter on listable types
            var query = _session.QueryIndexAsync<ContentItemIndex>();
            
            if (!string.IsNullOrEmpty(model.TypeName))
            {
                var contentTypeDefinition = _contentDefinitionManager.GetTypeDefinition(model.TypeName);
                if (contentTypeDefinition == null)
                    return HttpNotFound();

                model.TypeDisplayName = !string.IsNullOrWhiteSpace(contentTypeDefinition.DisplayName)
                                            ? contentTypeDefinition.DisplayName
                                            : contentTypeDefinition.Name;

                // We display a specific type even if it's not listable so that admin pages
                // can reuse the Content list page for specific types.
                query = query.With<ContentItemIndex>(x => x.ContentType == model.TypeName);
            }

            switch (model.Options.OrderBy)
            {
                case ContentsOrder.Modified:
                    query = query.OrderByDescending(x => x.ModifiedUtc);
                    break;
                case ContentsOrder.Published:
                    query = query.OrderByDescending(cr => cr.PublishedUtc);
                    break;
                case ContentsOrder.Created:
                    query = query.OrderByDescending(cr => cr.CreatedUtc);
                    break;
                default:
                    query = query.OrderByDescending(cr => cr.Id);
                    break;
            }

            //if (!String.IsNullOrWhiteSpace(model.Options.SelectedCulture))
            //{
            //    query = _cultureFilter.FilterCulture(query, model.Options.SelectedCulture);
            //}

            //if (model.Options.ContentsStatus == ContentsStatus.Owner)
            //{
            //    query = query.Where<CommonPartRecord>(cr => cr.OwnerId == Services.WorkContext.CurrentUser.Id);
            //}

            model.Options.SelectedFilter = model.TypeName;
            //model.Options.FilterOptions = GetListableTypes(false)
            //    .Select(ctd => new KeyValuePair<string, string>(ctd.Name, ctd.DisplayName))
            //    .ToList().OrderBy(kvp => kvp.Value);

            //model.Options.Cultures = _cultureManager.ListCultures();

            var maxPagedCount = siteSettings.MaxPagedCount;
            if (maxPagedCount > 0 && pager.PageSize > maxPagedCount)
                pager.PageSize = maxPagedCount;

            var pagerShape = New.Pager(pager).TotalItemCount(maxPagedCount > 0 ? maxPagedCount : await query.Count());
            var pageOfContentItemIndexes = await query.Skip(pager.GetStartIndex()).Take(pager.PageSize).List();
            var contentItems = await _session.GetAsync<ContentItem>(pageOfContentItemIndexes.Select(x => x.DocumentId));

            var viewModel = New.ViewModel()
                .ContentItems(contentItems)
                .Pager(pagerShape)
                .Options(model.Options)
                .TypeDisplayName(model.TypeDisplayName ?? "");

            return View(viewModel);
        }

        private IEnumerable<ContentTypeDefinition> GetCreatableTypes()
        {
            return _contentDefinitionManager.ListTypeDefinitions().Where(ctd =>
                //Services.Authorizer.Authorize(Permissions.EditContent, _contentManager.New(ctd.Name)) &&
                ctd.Settings.ToObject<ContentTypeSettings>().Creatable
                );
        }

        //private IEnumerable<ContentTypeDefinition> GetListableTypes(bool andContainable)
        //{
        //    return _contentDefinitionManager.ListTypeDefinitions().Where(ctd =>
        //        Services.Authorizer.Authorize(Permissions.EditContent, _contentManager.New(ctd.Name)) &&
        //        ctd.Settings.GetModel<ContentTypeSettings>().Listable &&
        //        (!andContainable || ctd.Parts.Any(p => p.PartDefinition.Name == "ContainablePart")));
        //}

        //[HttpPost, ActionName("List")]
        //[Mvc.FormValueRequired("submit.Filter")]
        //public ActionResult ListFilterPOST(ContentOptions options)
        //{
        //    var routeValues = ControllerContext.RouteData.Values;
        //    if (options != null)
        //    {
        //        routeValues["Options.SelectedCulture"] = options.SelectedCulture; //todo: don't hard-code the key
        //        routeValues["Options.OrderBy"] = options.OrderBy; //todo: don't hard-code the key
        //        routeValues["Options.ContentsStatus"] = options.ContentsStatus; //todo: don't hard-code the key
        //        if (GetListableTypes(false).Any(ctd => string.Equals(ctd.Name, options.SelectedFilter, StringComparison.OrdinalIgnoreCase)))
        //        {
        //            routeValues["id"] = options.SelectedFilter;
        //        }
        //        else {
        //            routeValues.Remove("id");
        //        }
        //    }

        //    return RedirectToAction("List", routeValues);
        //}

        //[HttpPost, ActionName("List")]
        //[Mvc.FormValueRequired("submit.BulkEdit")]
        //public ActionResult ListPOST(ContentOptions options, IEnumerable<int> itemIds, string returnUrl)
        //{
        //    if (itemIds != null)
        //    {
        //        var checkedContentItems = _contentManager.GetMany<ContentItem>(itemIds, VersionOptions.Latest, QueryHints.Empty);
        //        switch (options.BulkAction)
        //        {
        //            case ContentsBulkAction.None:
        //                break;
        //            case ContentsBulkAction.PublishNow:
        //                foreach (var item in checkedContentItems)
        //                {
        //                    if (!Services.Authorizer.Authorize(Permissions.PublishContent, item, T("Couldn't publish selected content.")))
        //                    {
        //                        _transactionManager.Cancel();
        //                        return new HttpUnauthorizedResult();
        //                    }

        //                    _contentManager.Publish(item);
        //                }
        //                Services.Notifier.Information(T("Content successfully published."));
        //                break;
        //            case ContentsBulkAction.Unpublish:
        //                foreach (var item in checkedContentItems)
        //                {
        //                    if (!Services.Authorizer.Authorize(Permissions.PublishContent, item, T("Couldn't unpublish selected content.")))
        //                    {
        //                        _transactionManager.Cancel();
        //                        return new HttpUnauthorizedResult();
        //                    }

        //                    _contentManager.Unpublish(item);
        //                }
        //                Services.Notifier.Information(T("Content successfully unpublished."));
        //                break;
        //            case ContentsBulkAction.Remove:
        //                foreach (var item in checkedContentItems)
        //                {
        //                    if (!Services.Authorizer.Authorize(Permissions.DeleteContent, item, T("Couldn't remove selected content.")))
        //                    {
        //                        _transactionManager.Cancel();
        //                        return new HttpUnauthorizedResult();
        //                    }

        //                    _contentManager.Remove(item);
        //                }
        //                Services.Notifier.Information(T("Content successfully removed."));
        //                break;
        //            default:
        //                throw new ArgumentOutOfRangeException();
        //        }
        //    }

        //    return this.RedirectLocal(returnUrl, () => RedirectToAction("List"));
        //}

        //ActionResult ListableTypeList(int? containerId)
        //{
        //    var viewModel = Shape.ViewModel(ContentTypes: GetListableTypes(containerId.HasValue), ContainerId: containerId);

        //    return View("ListableTypeList", viewModel);
        //}

        public async Task<IActionResult> Create(string id)
        {
            if (String.IsNullOrWhiteSpace(id))
            {
                return HttpNotFound();
            }

            var contentItem = _contentManager.New(id);

            //if (!Services.Authorizer.Authorize(Permissions.EditContent, contentItem, T("Cannot create content")))
            //    return new HttpUnauthorizedResult();

            var model = await _contentItemDisplayManager.BuildEditorAsync(contentItem);

            return View(model);
        }

        [HttpPost, ActionName("Create")]
        [FormValueRequired("submit.Save")]
        public Task<IActionResult> CreatePOST(string id, string returnUrl)
        {
            return CreatePOST(id, returnUrl, async contentItem =>
            {
                var contentTypeDefinition = _contentDefinitionManager.GetTypeDefinition(id);

                if (!contentTypeDefinition.Settings.ToObject<ContentTypeSettings>().Draftable)
                {
                    await _contentManager.PublishAsync(contentItem);
                }
            });
        }

        [HttpPost, ActionName("Create")]
        [FormValueRequired("submit.Publish")]
        public Task<IActionResult> CreateAndPublishPOST(string id, string returnUrl)
        {

            // pass a dummy content to the authorization check to check for "own" variations
            var dummyContent = _contentManager.New(id);

            //if (!Services.Authorizer.Authorize(Permissions.PublishContent, dummyContent, T("Couldn't create content")))
            //    return new HttpUnauthorizedResult();

            return CreatePOST(id, returnUrl, async contentItem => await _contentManager.PublishAsync(contentItem));
        }

        private async Task<IActionResult> CreatePOST(string id, string returnUrl, Func<ContentItem, Task> conditionallyPublish)
        {
            var contentItem = _contentManager.New(id);

            //if (!Services.Authorizer.Authorize(Permissions.EditContent, contentItem, T("Couldn't create content")))
            //    return new HttpUnauthorizedResult();

            _contentManager.Create(contentItem, VersionOptions.Draft);

            var model = await _contentItemDisplayManager.UpdateEditorAsync(contentItem, this);

            if (!ModelState.IsValid)
            {
                _session.Cancel();
                return View(model);
            }

            await conditionallyPublish(contentItem);

            //Services.Notifier.Information(string.IsNullOrWhiteSpace(contentItem.TypeDefinition.DisplayName)
            //    ? T("Your content has been created.")
            //    : T("Your {0} has been created.", contentItem.TypeDefinition.DisplayName));

            if (!string.IsNullOrEmpty(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            var adminRouteValues = _contentManager.GetItemMetadata(contentItem).AdminRouteValues;
            return RedirectToRoute(adminRouteValues);
        }

        public async Task<IActionResult> Edit(int id)
        {
            var contentItem = await _contentManager.GetAsync(id, VersionOptions.Latest);

            if (contentItem == null)
                return HttpNotFound();

            //if (!Services.Authorizer.Authorize(Permissions.EditContent, contentItem, T("Cannot edit content")))
            //    return new HttpUnauthorizedResult();

            var model = await _contentItemDisplayManager.BuildEditorAsync(contentItem);

            return View(model);
        }

        [HttpPost, ActionName("Edit")]
        [FormValueRequired("submit.Save")]
        public Task<IActionResult> EditPOST(int id, string returnUrl)
        {
            return EditPOST(id, returnUrl, async contentItem =>
            {
                var contentTypeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                if (!contentTypeDefinition.Settings.ToObject<ContentTypeSettings>().Draftable)
                    await _contentManager.PublishAsync(contentItem);
            });
        }

        [HttpPost, ActionName("Edit")]
        [Mvc.FormValueRequired("submit.Publish")]
        public async Task<IActionResult> EditAndPublishPOST(int id, string returnUrl)
        {
            var content = await _contentManager.GetAsync(id, VersionOptions.Latest);

            if (content == null)
                return HttpNotFound();

            //if (!Services.Authorizer.Authorize(Permissions.PublishContent, content, T("Couldn't publish content")))
            //    return new HttpUnauthorizedResult();

            return await EditPOST(id, returnUrl, async contentItem => await _contentManager.PublishAsync(contentItem));
        }

        private async Task<IActionResult> EditPOST(int id, string returnUrl, Func<ContentItem, Task> conditionallyPublish)
        {
            var contentItem = await _contentManager.GetAsync(id, VersionOptions.DraftRequired);

            if (contentItem == null)
                return HttpNotFound();

            //if (!Services.Authorizer.Authorize(Permissions.EditContent, contentItem, T("Couldn't edit content")))
            //    return new HttpUnauthorizedResult();

            string previousRoute = null;
            //if (contentItem.Has<IAliasAspect>() && 
            //    !string.IsNullOrWhiteSpace(returnUrl)
            //    && Request.IsLocalUrl(returnUrl)
            //    // only if the original returnUrl is the content itself
            //    && String.Equals(returnUrl, Url.ItemDisplayUrl(contentItem), StringComparison.OrdinalIgnoreCase)
            //    )
            //{
            //    previousRoute = contentItem.As<IAliasAspect>().Path;
            //}

            var model = await _contentItemDisplayManager.UpdateEditorAsync(contentItem, this);
            if (!ModelState.IsValid)
            {
                _session.Cancel();
                return View("Edit", model);
            }

            await conditionallyPublish(contentItem);

            //if (!string.IsNullOrWhiteSpace(returnUrl)
            //    && previousRoute != null
            //    && !String.Equals(contentItem.As<IAliasAspect>().Path, previousRoute, StringComparison.OrdinalIgnoreCase))
            //{
            //    returnUrl = Url.ItemDisplayUrl(contentItem);
            //}

            //Services.Notifier.Information(string.IsNullOrWhiteSpace(contentItem.TypeDefinition.DisplayName)
            //    ? T("Your content has been saved.")
            //    : T("Your {0} has been saved.", contentItem.TypeDefinition.DisplayName));

            if (returnUrl == null)
            {
                return RedirectToAction("Edit", new RouteValueDictionary { { "Id", contentItem.ContentItemId } });
            }
            else
            {
                return LocalRedirect(returnUrl);
            }
        }

        //[HttpPost]
        //public ActionResult Clone(int id, string returnUrl)
        //{
        //    var contentItem = _contentManager.GetLatest(id);

        //    if (contentItem == null)
        //        return HttpNotFound();

        //    if (!Services.Authorizer.Authorize(Permissions.EditContent, contentItem, T("Couldn't clone content")))
        //        return new HttpUnauthorizedResult();

        //    try
        //    {
        //        Services.ContentManager.Clone(contentItem);
        //    }
        //    catch (InvalidOperationException)
        //    {
        //        Services.Notifier.Warning(T("Could not clone the content item."));
        //        return this.RedirectLocal(returnUrl, () => RedirectToAction("List"));
        //    }

        //    Services.Notifier.Information(T("Successfully cloned. The clone was saved as a draft."));

        //    return this.RedirectLocal(returnUrl, () => RedirectToAction("List"));
        //}

        //[HttpPost]
        //public ActionResult Remove(int id, string returnUrl)
        //{
        //    var contentItem = _contentManager.Get(id, VersionOptions.Latest);

        //    if (!Services.Authorizer.Authorize(Permissions.DeleteContent, contentItem, T("Couldn't remove content")))
        //        return new HttpUnauthorizedResult();

        //    if (contentItem != null)
        //    {
        //        _contentManager.Remove(contentItem);
        //        Services.Notifier.Information(string.IsNullOrWhiteSpace(contentItem.TypeDefinition.DisplayName)
        //            ? T("That content has been removed.")
        //            : T("That {0} has been removed.", contentItem.TypeDefinition.DisplayName));
        //    }

        //    return this.RedirectLocal(returnUrl, () => RedirectToAction("List"));
        //}

        //[HttpPost]
        //public ActionResult Publish(int id, string returnUrl)
        //{
        //    var contentItem = _contentManager.GetLatest(id);
        //    if (contentItem == null)
        //        return HttpNotFound();

        //    if (!Services.Authorizer.Authorize(Permissions.PublishContent, contentItem, T("Couldn't publish content")))
        //        return new HttpUnauthorizedResult();

        //    _contentManager.Publish(contentItem);

        //    Services.Notifier.Information(string.IsNullOrWhiteSpace(contentItem.TypeDefinition.DisplayName) ? T("That content has been published.") : T("That {0} has been published.", contentItem.TypeDefinition.DisplayName));

        //    return this.RedirectLocal(returnUrl, () => RedirectToAction("List"));
        //}

        //[HttpPost]
        //public ActionResult Unpublish(int id, string returnUrl)
        //{
        //    var contentItem = _contentManager.GetLatest(id);
        //    if (contentItem == null)
        //        return HttpNotFound();

        //    if (!Services.Authorizer.Authorize(Permissions.PublishContent, contentItem, T("Couldn't unpublish content")))
        //        return new HttpUnauthorizedResult();

        //    _contentManager.Unpublish(contentItem);

        //    Services.Notifier.Information(string.IsNullOrWhiteSpace(contentItem.TypeDefinition.DisplayName) ? T("That content has been unpublished.") : T("That {0} has been unpublished.", contentItem.TypeDefinition.DisplayName));

        //    return this.RedirectLocal(returnUrl, () => RedirectToAction("List"));
        //}

        //bool IUpdateModel.TryUpdateModel<TModel>(TModel model, string prefix, string[] includeProperties, string[] excludeProperties)
        //{
        //    return TryUpdateModel(model, prefix, includeProperties, excludeProperties);
        //}

        //void IUpdateModel.AddModelError(string key, LocalizedString errorMessage)
        //{
        //    ModelState.AddModelError(key, errorMessage.ToString());
        //}
    }
}
