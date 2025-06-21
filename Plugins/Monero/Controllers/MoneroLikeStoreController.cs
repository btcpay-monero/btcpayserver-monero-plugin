using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Monero.Configuration;
using BTCPayServer.Plugins.Monero.Payments;
using BTCPayServer.Plugins.Monero.RPC.Models;
using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Monero.Controllers
{
    [Route("stores/{storeId}/monerolike")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIMoneroLikeStoreController : Controller
    {
        private readonly MoneroLikeConfiguration _MoneroLikeConfiguration;
        private readonly StoreRepository _StoreRepository;
        private readonly MoneroRPCProvider _MoneroRpcProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private IStringLocalizer StringLocalizer { get; }

        public UIMoneroLikeStoreController(MoneroLikeConfiguration moneroLikeConfiguration,
            StoreRepository storeRepository, MoneroRPCProvider moneroRpcProvider,
            PaymentMethodHandlerDictionary handlers,
            IStringLocalizer stringLocalizer)
        {
            _MoneroLikeConfiguration = moneroLikeConfiguration;
            _StoreRepository = storeRepository;
            _MoneroRpcProvider = moneroRpcProvider;
            _handlers = handlers;
            StringLocalizer = stringLocalizer;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethods()
        {
            return View("/Views/Monero/GetStoreMoneroLikePaymentMethods.cshtml", await GetVM(StoreData));
        }
        [NonAction]
        public async Task<MoneroLikePaymentMethodListViewModel> GetVM(StoreData storeData)
        {
            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

            var accountsList = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.ToDictionary(pair => pair.Key,
                pair => GetAccounts(pair.Key));

            await Task.WhenAll(accountsList.Values);
            return new MoneroLikePaymentMethodListViewModel()
            {
                Items = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.Select(pair =>
                    GetMoneroLikePaymentMethodViewModel(storeData, pair.Key, excludeFilters,
                        accountsList[pair.Key].Result))
            };
        }

        private Task<GetAccountsResponse> GetAccounts(string cryptoCode)
        {
            try
            {
                if (_MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary) && summary.WalletAvailable)
                {

                    return _MoneroRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<GetAccountsRequest, GetAccountsResponse>("get_accounts", new GetAccountsRequest());
                }
            }
            catch
            {
                // ignored
            }

            return Task.FromResult<GetAccountsResponse>(null);
        }

        private MoneroLikePaymentMethodViewModel GetMoneroLikePaymentMethodViewModel(
            StoreData storeData, string cryptoCode,
            IPaymentFilter excludeFilters, GetAccountsResponse accountsResponse)
        {
            var monero = storeData.GetPaymentMethodConfigs(_handlers)
                .Where(s => s.Value is MoneroPaymentPromptDetails)
                .Select(s => (PaymentMethodId: s.Key, Details: (MoneroPaymentPromptDetails)s.Value));
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var settings = monero.Where(method => method.PaymentMethodId == pmi).Select(m => m.Details).SingleOrDefault();
            _MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
            _MoneroLikeConfiguration.MoneroLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem);
            var accounts = accountsResponse?.SubaddressAccounts?.Select(account =>
                new SelectListItem(
                    $"{account.AccountIndex} - {(string.IsNullOrEmpty(account.Label) ? "No label" : account.Label)}",
                    account.AccountIndex.ToString(CultureInfo.InvariantCulture)));

            var settlementThresholdChoice = MoneroLikeSettlementThresholdChoice.StoreSpeedPolicy;
            if (settings != null && settings.InvoiceSettledConfirmationThreshold is { } confirmations)
            {
                settlementThresholdChoice = confirmations switch
                {
                    0 => MoneroLikeSettlementThresholdChoice.ZeroConfirmation,
                    1 => MoneroLikeSettlementThresholdChoice.AtLeastOne,
                    10 => MoneroLikeSettlementThresholdChoice.AtLeastTen,
                    _ => MoneroLikeSettlementThresholdChoice.Custom
                };
            }

            return new MoneroLikePaymentMethodViewModel()
            {
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)),
                Summary = summary,
                CryptoCode = cryptoCode,
                AccountIndex = settings?.AccountIndex ?? accountsResponse?.SubaddressAccounts?.FirstOrDefault()?.AccountIndex ?? 0,
                Accounts = accounts == null ? null : new SelectList(accounts, nameof(SelectListItem.Value),
                    nameof(SelectListItem.Text)),
                SettlementConfirmationThresholdChoice = settlementThresholdChoice,
                CustomSettlementConfirmationThreshold =
                    settings != null &&
                    settlementThresholdChoice is MoneroLikeSettlementThresholdChoice.Custom
                        ? settings.InvoiceSettledConfirmationThreshold
                        : null
            };
        }

        [HttpGet("{cryptoCode}")]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethod(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode,
                StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));
            return View("/Views/Monero/GetStoreMoneroLikePaymentMethod.cshtml", vm);
        }

        [HttpPost("{cryptoCode}")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethod(MoneroLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem))
            {
                return NotFound();
            }

            if (command == "add-account")
            {
                try
                {
                    var newAccount = await _MoneroRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<CreateAccountRequest, CreateAccountResponse>("create_account", new CreateAccountRequest()
                    {
                        Label = viewModel.NewAccountLabel
                    });
                    viewModel.AccountIndex = newAccount.AccountIndex;
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(viewModel.AccountIndex), StringLocalizer["Could not create a new account."]);
                }

            }
            else if (command == "set-wallet-details")
            {
                var valid = true;
                if (viewModel.PrimaryAddress == null)
                {
                    ModelState.AddModelError(nameof(viewModel.PrimaryAddress), StringLocalizer["Please set your primary public address"]);
                    valid = false;
                }
                if (viewModel.PrivateViewKey == null)
                {
                    ModelState.AddModelError(nameof(viewModel.PrivateViewKey), StringLocalizer["Please set your private view key"]);
                    valid = false;
                }
                if (configurationItem.WalletDirectory == null)
                {
                    ModelState.AddModelError(nameof(viewModel.PrimaryAddress), StringLocalizer["This installation doesn't support wallet creation (BTCPAY_XMR_WALLET_DAEMON_WALLETDIR is not set)"]);
                    valid = false;
                }
                if (valid)
                {
                    if (_MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary))
                    {
                        if (summary.WalletAvailable)
                        {
                            TempData.SetStatusMessageModel(new StatusMessageModel
                            {
                                Severity = StatusMessageModel.StatusSeverity.Error,
                                Message = StringLocalizer["There is already an active wallet configured for {0}. Replacing it would break any existing invoices!", cryptoCode].Value
                            });
                            return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod),
                                new { cryptoCode });
                        }
                    }
                    try
                    {
                        var response = await _MoneroRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<GenerateFromKeysRequest, GenerateFromKeysResponse>("generate_from_keys", new GenerateFromKeysRequest
                        {
                            PrimaryAddress = viewModel.PrimaryAddress,
                            PrivateViewKey = viewModel.PrivateViewKey,
                            WalletFileName = "view_wallet",
                            RestoreHeight = viewModel.RestoreHeight,
                            Password = viewModel.WalletPassword
                        });
                        if (response?.Error != null)
                        {
                            throw new GenerateFromKeysException(response.Error.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError(nameof(viewModel.AccountIndex), StringLocalizer["Could not generate view wallet from keys: {0}", ex.Message]);
                        return View("/Views/Monero/GetStoreMoneroLikePaymentMethod.cshtml", viewModel);
                    }

                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Info,
                        Message = StringLocalizer["View-only wallet created. The wallet will soon become available."].Value
                    });
                    return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { cryptoCode });
                }
            }

            if (!ModelState.IsValid)
            {

                var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode,
                    StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));

                vm.Enabled = viewModel.Enabled;
                vm.NewAccountLabel = viewModel.NewAccountLabel;
                vm.AccountIndex = viewModel.AccountIndex;
                vm.SettlementConfirmationThresholdChoice = viewModel.SettlementConfirmationThresholdChoice;
                vm.CustomSettlementConfirmationThreshold = viewModel.CustomSettlementConfirmationThreshold;
                return View("/Views/Monero/GetStoreMoneroLikePaymentMethod.cshtml", vm);
            }

            var storeData = StoreData;
            var blob = storeData.GetStoreBlob();
            storeData.SetPaymentMethodConfig(_handlers[PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)], new MoneroPaymentPromptDetails()
            {
                AccountIndex = viewModel.AccountIndex,
                InvoiceSettledConfirmationThreshold = viewModel.SettlementConfirmationThresholdChoice switch
                {
                    MoneroLikeSettlementThresholdChoice.ZeroConfirmation => 0,
                    MoneroLikeSettlementThresholdChoice.AtLeastOne => 1,
                    MoneroLikeSettlementThresholdChoice.AtLeastTen => 10,
                    MoneroLikeSettlementThresholdChoice.Custom when viewModel.CustomSettlementConfirmationThreshold is { } custom => custom,
                    _ => null
                }
            });

            blob.SetExcluded(PaymentTypes.CHAIN.GetPaymentMethodId(viewModel.CryptoCode), !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreMoneroLikePaymentMethods",
                new { StatusMessage = $"{cryptoCode} settings updated successfully", storeId = StoreData.Id });
        }

        public class MoneroLikePaymentMethodListViewModel
        {
            public IEnumerable<MoneroLikePaymentMethodViewModel> Items { get; set; }
        }

        public class MoneroLikePaymentMethodViewModel : IValidatableObject
        {
            public MoneroRPCProvider.MoneroLikeSummary Summary { get; set; }
            public string CryptoCode { get; set; }
            public string NewAccountLabel { get; set; }
            public long AccountIndex { get; set; }
            public bool Enabled { get; set; }

            public IEnumerable<SelectListItem> Accounts { get; set; }
            public bool WalletFileFound { get; set; }
            [Display(Name = "Primary Public Address")]
            public string PrimaryAddress { get; set; }
            [Display(Name = "Private View Key")]
            public string PrivateViewKey { get; set; }
            [Display(Name = "Restore Height")]
            public int RestoreHeight { get; set; }
            [Display(Name = "Wallet Password")]
            public string WalletPassword { get; set; }
            [Display(Name = "Consider the invoice settled when the payment transaction …")]
            public MoneroLikeSettlementThresholdChoice SettlementConfirmationThresholdChoice { get; set; }
            [Display(Name = "Required Confirmations"), Range(0, 100)]
            public long? CustomSettlementConfirmationThreshold { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (SettlementConfirmationThresholdChoice is MoneroLikeSettlementThresholdChoice.Custom
                    && CustomSettlementConfirmationThreshold is null)
                {
                    yield return new ValidationResult(
                        "You must specify the number of required confirmations when using a custom threshold.",
                        new[] { nameof(CustomSettlementConfirmationThreshold) });
                }
            }
        }

        public enum MoneroLikeSettlementThresholdChoice
        {
            [Display(Name = "Store Speed Policy", Description = "Use the store's speed policy")]
            StoreSpeedPolicy,
            [Display(Name = "Zero Confirmation", Description = "Is unconfirmed")]
            ZeroConfirmation,
            [Display(Name = "At Least One", Description = "Has at least 1 confirmation")]
            AtLeastOne,
            [Display(Name = "At Least Ten", Description = "Has at least 10 confirmations")]
            AtLeastTen,
            [Display(Name = "Custom", Description = "Custom")]
            Custom
        }
    }
}