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
        private readonly MoneroRpcProvider _MoneroRpcProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly MoneroWalletService _walletService;
        private IStringLocalizer StringLocalizer { get; }

        public UIMoneroLikeStoreController(MoneroLikeConfiguration moneroLikeConfiguration,
            StoreRepository storeRepository, MoneroRpcProvider MoneroRpcProvider,
            PaymentMethodHandlerDictionary handlers,
            MoneroWalletService walletService,
            IStringLocalizer stringLocalizer)
        {
            _MoneroLikeConfiguration = moneroLikeConfiguration;
            _StoreRepository = storeRepository;
            _MoneroRpcProvider = MoneroRpcProvider;
            _handlers = handlers;
            _walletService = walletService;
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
            var accountsList = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.ToDictionary(pair => pair.Key,
                pair => _MoneroRpcProvider.GetAccounts(pair.Key));

            await Task.WhenAll(accountsList.Values);
            return new MoneroLikePaymentMethodListViewModel()
            {
                Items = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.Select(pair =>
                    GetMoneroLikePaymentMethodViewModel(storeData, pair.Key, storeData.GetStoreBlob().GetExcludedPaymentMethods(),
                        accountsList[pair.Key].Result))
            };
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

            MoneroWalletState walletState = _walletService.GetWalletState();

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
                        : null,

                WalletState = walletState,
                HasDeprecatedPasswordFile = _walletService.HasDeprecatedPasswordFile()
            };
        }

        private async Task<bool> ValidateAndCreateWallet(
            MoneroLikePaymentMethodViewModel viewModel, string cryptoCode)
        {
            if (string.IsNullOrWhiteSpace(viewModel.WalletName))
            {
                ModelState.AddModelError(string.Empty, StringLocalizer["A wallet name is required to create a new wallet."]);
                return false;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(viewModel.WalletName, "^[a-zA-Z0-9_-]+$") ||
                viewModel.WalletName.Length > 64)
            {
                ModelState.AddModelError(string.Empty, StringLocalizer["Wallet name must contain only letters, numbers, dashes, and underscores (max 64 characters)."]);
                return false;
            }

            // TODO: Validate shape of primary address and private view key
            if (string.IsNullOrEmpty(viewModel.PrimaryAddress))
            {
                ModelState.AddModelError(string.Empty, StringLocalizer["The primary address is required to create a new wallet."]);
                return false;
            }

            if (string.IsNullOrEmpty(viewModel.PrivateViewKey))
            {
                ModelState.AddModelError(string.Empty, StringLocalizer["The private view key is required to create a new wallet."]);
                return false;
            }

            if (!ModelState.IsValid)
            {
                return false;
            }

            MoneroWalletState walletState = _walletService.GetWalletState();

            if (_MoneroRpcProvider.GetWalletList(cryptoCode).Contains(viewModel.WalletName))
            {
                ModelState.AddModelError(string.Empty, StringLocalizer["A wallet with the name {0} already exists. Please choose a different name.", viewModel.WalletName]);
                return false;
            }
            if (walletState.Wallets.TryGetValue(viewModel.PrimaryAddress, out var existingWallet))
            {
                ModelState.AddModelError(string.Empty, StringLocalizer["This wallet is already imported as {0}. A wallet cannot be imported more than once.", existingWallet]);
                return false;
            }

            var (success, errorMessage) = await _walletService.CreateAndActivateWallet(
                viewModel.WalletName,
                viewModel.PrimaryAddress,
                viewModel.PrivateViewKey,
                viewModel.RestoreHeight,
                StoreData.Id);

            if (!success)
            {
                ModelState.AddModelError(string.Empty, StringLocalizer["Could not create wallet: {0}", errorMessage]);
            }

            return success;
        }

        [HttpGet("setup/{cryptoCode}")]
        public async Task<IActionResult> SetupMoneroWallet(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            GetAccountsResponse accounts = await _MoneroRpcProvider.GetAccounts(cryptoCode);
            var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode, StoreData.GetStoreBlob().GetExcludedPaymentMethods(), accounts);

            return View("/Views/Monero/SetupMoneroWallet.cshtml", vm);
        }

        [HttpGet("connect/{cryptoCode}")]
        public async Task<IActionResult> ConnectNewWallet(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            GetAccountsResponse accounts = await _MoneroRpcProvider.GetAccounts(cryptoCode);
            var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode, StoreData.GetStoreBlob().GetExcludedPaymentMethods(), accounts);

            return View("/Views/Monero/ConnectNewWallet.cshtml", vm);
        }

        [HttpPost("connect/{cryptoCode}")]
        public async Task<IActionResult> ConnectNewWallet(MoneroLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            if (command == "connect-wallet")
            {
                if (await ValidateAndCreateWallet(viewModel, cryptoCode))
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Success,
                        Message = StringLocalizer["Wallet {0} created successfully and now active", viewModel.WalletName].Value
                    });

                    return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
                }
            }

            if (!ModelState.IsValid)
            {
                GetAccountsResponse accounts = await _MoneroRpcProvider.GetAccounts(cryptoCode);
                var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode, StoreData.GetStoreBlob().GetExcludedPaymentMethods(), accounts);
                vm.WalletName = viewModel.WalletName;
                vm.PrimaryAddress = viewModel.PrimaryAddress;
                vm.PrivateViewKey = viewModel.PrivateViewKey;
                vm.RestoreHeight = viewModel.RestoreHeight;
                return View("/Views/Monero/ConnectNewWallet.cshtml", vm);
            }

            return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
        }

        [HttpGet("{cryptoCode}")]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethod(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            GetAccountsResponse accounts = await _MoneroRpcProvider.GetAccounts(cryptoCode);
            var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode, StoreData.GetStoreBlob().GetExcludedPaymentMethods(), accounts);

            return View("/Views/Monero/GetStoreMoneroLikePaymentMethod.cshtml", vm);
        }

        [HttpPost("{cryptoCode}")]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethod(MoneroLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            if (command == "add-account")
            {
                try
                {
                    CreateAccountResponse newAccount = await _MoneroRpcProvider.CreateAccount(cryptoCode, viewModel.NewAccountLabel);
                    if (newAccount != null)
                    {
                        viewModel.AccountIndex = newAccount.AccountIndex;
                    }
                }
                catch (Exception)
                {
                    ModelState.AddModelError(string.Empty, StringLocalizer["Could not create a new account."]);
                }

            }
            else if (command == "set-active-wallet")
            {
                if (string.IsNullOrWhiteSpace(viewModel.NewActiveWallet))
                {
                    ModelState.AddModelError(string.Empty, StringLocalizer["Please select a wallet"]);
                }
                else
                {
                    var (success, errorMessage) = await _walletService.SetActiveWallet(viewModel.NewActiveWallet, StoreData.Id);

                    if (success)
                    {
                        var walletName = _walletService.GetWalletState().ActiveWalletName;
                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Severity = StatusMessageModel.StatusSeverity.Success,
                            Message = StringLocalizer["Wallet changed to {0}", walletName].Value
                        });
                        return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
                    }
                    ModelState.AddModelError(string.Empty, StringLocalizer["Failed to set active wallet: {0}", errorMessage]);
                }
            }
            else if (command == "add-wallet")
            {
                if (await ValidateAndCreateWallet(viewModel, cryptoCode))
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Success,
                        Message = StringLocalizer["New wallet {0} created and activated", viewModel.WalletName].Value
                    });

                    return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
                }
            }
            else if (command == "delete-wallet")
            {
                if (string.IsNullOrWhiteSpace(viewModel.WalletToDelete))
                {
                    ModelState.AddModelError(string.Empty, StringLocalizer["No wallet provided."]);
                }
                else
                {
                    MoneroWalletState walletState = _walletService.GetWalletState();
                    string walletName = walletState.Wallets.GetValueOrDefault(viewModel.WalletToDelete);

                    var (success, errorMessage) = await _walletService.DeleteWallet(viewModel.WalletToDelete);
                    if (success)
                    {
                        if (viewModel.WalletToDelete == walletState.ActiveWalletAddress)
                        {
                            await DisableMoneroAcrossStores(cryptoCode);
                        }

                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Severity = StatusMessageModel.StatusSeverity.Success,
                            Message = StringLocalizer["Wallet {0} has been deleted.", walletName!].Value
                        });

                        if (!_walletService.GetWalletState().Wallets.Any())
                        {
                            return RedirectToAction(nameof(SetupMoneroWallet), new { storeId = StoreData.Id, cryptoCode });
                        }
                    }
                    else
                    {
                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Severity = StatusMessageModel.StatusSeverity.Error,
                            Message = StringLocalizer["Failed to delete wallet: {0}", errorMessage].Value
                        });
                    }

                    return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
                }
            }

            if (!ModelState.IsValid)
            {
                GetAccountsResponse accounts = await _MoneroRpcProvider.GetAccounts(cryptoCode);
                var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode, StoreData.GetStoreBlob().GetExcludedPaymentMethods(), accounts);

                vm.Enabled = viewModel.Enabled;
                vm.NewAccountLabel = viewModel.NewAccountLabel;
                vm.AccountIndex = viewModel.AccountIndex;
                vm.SettlementConfirmationThresholdChoice = viewModel.SettlementConfirmationThresholdChoice;
                vm.CustomSettlementConfirmationThreshold = viewModel.CustomSettlementConfirmationThreshold;
                return View("/Views/Monero/GetStoreMoneroLikePaymentMethod.cshtml", vm);
            }

            StoreData storeData = StoreData;
            StoreBlob blob = storeData.GetStoreBlob();
            PaymentMethodId pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);

            var existingSettings = storeData.GetPaymentMethodConfig<MoneroPaymentPromptDetails>(pmi, _handlers);
            var settings = existingSettings ?? new MoneroPaymentPromptDetails();

            settings.AccountIndex = viewModel.AccountIndex;

            settings.InvoiceSettledConfirmationThreshold = viewModel.SettlementConfirmationThresholdChoice switch
            {
                MoneroLikeSettlementThresholdChoice.ZeroConfirmation => 0,
                MoneroLikeSettlementThresholdChoice.AtLeastOne => 1,
                MoneroLikeSettlementThresholdChoice.AtLeastTen => 10,
                MoneroLikeSettlementThresholdChoice.Custom when viewModel.CustomSettlementConfirmationThreshold is { } custom => custom,
                _ => null
            };

            storeData.SetPaymentMethodConfig(_handlers[pmi], settings);

            blob.SetExcluded(PaymentTypes.CHAIN.GetPaymentMethodId(viewModel.CryptoCode), !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreMoneroLikePaymentMethods",
                new { StatusMessage = $"{cryptoCode} settings updated successfully", storeId = StoreData.Id });
        }

        private async Task DisableMoneroAcrossStores(string cryptoCode)
        {
            PaymentMethodId pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            IEnumerable<StoreData> allStores = await _StoreRepository.GetStores();

            foreach (StoreData store in allStores)
            {
                StoreBlob blob = store.GetStoreBlob();
                if (!blob.IsExcluded(pmi))
                {
                    blob.SetExcluded(pmi, true);
                    store.SetStoreBlob(blob);
                    await _StoreRepository.UpdateStore(store);
                }
            }
        }

        public class MoneroLikePaymentMethodListViewModel
        {
            public IEnumerable<MoneroLikePaymentMethodViewModel> Items { get; set; }
        }

        public class MoneroLikePaymentMethodViewModel : IValidatableObject
        {
            public MoneroRpcProvider.MoneroLikeSummary Summary { get; set; }
            public string CryptoCode { get; set; }
            public string NewAccountLabel { get; set; }
            public long AccountIndex { get; set; }
            public bool Enabled { get; set; }
            public IEnumerable<SelectListItem> Accounts { get; set; }
            [Display(Name = "Primary Public Address")]
            public string PrimaryAddress { get; set; }
            [Display(Name = "Private View Key")]
            public string PrivateViewKey { get; set; }
            [Display(Name = "Restore Height")]
            public int RestoreHeight { get; set; }
            [Display(Name = "Wallet Name")]
            public string WalletName { get; set; }
            [Display(Name = "Consider the invoice settled when the payment transaction …")]
            public MoneroLikeSettlementThresholdChoice SettlementConfirmationThresholdChoice { get; set; }
            [Display(Name = "Required Confirmations"), Range(0, 100)]
            public long? CustomSettlementConfirmationThreshold { get; set; }
            [Display(Name = "Select Active Wallet")]
            public string NewActiveWallet { get; set; }
            [Display(Name = "Wallet to Delete")]
            public string WalletToDelete { get; set; }
            public MoneroWalletState WalletState { get; set; }

            public bool HasDeprecatedPasswordFile { get; set; }
            public bool HasActiveWallet => WalletState?.ActiveWalletName != null;

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