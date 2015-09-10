﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc;
using Octokit;
using Octokit.Internal;
using ProjectKIssueList.Models;

namespace ProjectKIssueList.Controllers
{
    public class IssueListController : Controller
    {
        private GitHubClient GetGitHubClient(string gitHubAccessToken)
        {
            var ghc = new GitHubClient(
                new ProductHeaderValue("Project-K-Issue-List"),
                new InMemoryCredentialStore(new Credentials(gitHubAccessToken)));

            return ghc;
        }

        private Task<IReadOnlyList<Issue>> GetIssuesForRepo(string repo, GitHubClient gitHubClient)
        {
            var repositoryIssueRequest = new RepositoryIssueRequest
            {
                State = ItemState.Open,
            };

            return gitHubClient.Issue.GetAllForRepository("aspnet", repo, repositoryIssueRequest);
        }

        private Task<IReadOnlyList<PullRequest>> GetPullRequestsForRepo(string repo, GitHubClient gitHubClient)
        {
            return gitHubClient.PullRequest.GetAllForRepository("aspnet", repo);
        }

        private static readonly string[] ExcludedRepos = new[] {
            "Backlog",
            "Discussion",
            "Discussions",
        };

        private static bool IsExcludedRepo(string repoName)
        {
            return ExcludedRepos.Contains(repoName, StringComparer.OrdinalIgnoreCase);
        }

        [Route("{repoSet}")]
        [GitHubAuthData]
        public IActionResult Index(string repoSet, string gitHubAccessToken, string gitHubName)
        {
            // Authenticated and all claims have been read

            var repos =
                RepoSets.HasRepoSet(repoSet ?? string.Empty)
                ? RepoSets.GetRepoSet(repoSet)
                : RepoSets.GetAllRepos();

            var allIssuesByRepo = new ConcurrentDictionary<string, Task<IReadOnlyList<Issue>>>();
            var allPullRequestsByRepo = new ConcurrentDictionary<string, Task<IReadOnlyList<PullRequest>>>();

            Parallel.ForEach(repos, repo => allIssuesByRepo[repo] = GetIssuesForRepo(repo, GetGitHubClient(gitHubAccessToken)));
            Parallel.ForEach(repos, repo => allPullRequestsByRepo[repo] = GetPullRequestsForRepo(repo, GetGitHubClient(gitHubAccessToken)));

            Task.WaitAll(allIssuesByRepo.Select(x => x.Value).ToArray());
            Task.WaitAll(allPullRequestsByRepo.Select(x => x.Value).ToArray());

            var allIssues = allIssuesByRepo.SelectMany(
                issueList => issueList.Value.Result
                .Where(issue => !IsExcludedRepo(issue.Milestone?.Title))
                .Select(
                    issue => new IssueWithRepo
                    {
                        Issue = issue,
                        RepoName = issueList.Key,
                    })).ToList();

            var workingIssues = allIssues
                .Where(issue => issue.Issue.Labels.Any(label => label.Name == "2 - Working")).ToList();

            var untriagedIssues = allIssues
                .Where(issue => issue.Issue.Milestone == null).ToList();

            var allPullRequests = allPullRequestsByRepo.SelectMany(
                pullRequestList => pullRequestList.Value.Result.Select(
                    pullRequest => new PullRequestWithRepo
                    {
                        PullRequest = pullRequest,
                        RepoName = pullRequestList.Key,
                    }))
                    .OrderBy(pullRequestWithRepo => pullRequestWithRepo.PullRequest.CreatedAt)
                    .ToList();

            return View(new IssueListViewModel
            {
                Name = gitHubName,

                TotalIssues = allIssues.Count,
                WorkingIssues = workingIssues.Count,
                UntriagedIssues = untriagedIssues.Count,

                ReposIncluded = repos.OrderBy(repo => repo.ToLowerInvariant()).ToArray(),

                GroupByAssignee = new GroupByAssigneeViewModel
                {
                    Assignees =
                        workingIssues
                            .GroupBy(issue => issue.Issue.Assignee?.Login)
                            .Select(group =>
                                new GroupByAssigneeAssignee
                                {
                                    Assignee = group.Key,
                                    Issues = group.ToList().AsReadOnly(),
                                })
                            .OrderBy(group => group.Assignee, StringComparer.OrdinalIgnoreCase)
                            .ToList()
                            .AsReadOnly()
                },

                GroupByMilestone = new GroupByMilestoneViewModel
                {
                    Milestones =
                        workingIssues
                            .GroupBy(issue => issue.Issue.Milestone?.Title)
                            .Select(group =>
                                new GroupByMilestoneMilestone
                                {
                                    Milestone = group.Key,
                                    Issues = group.ToList().AsReadOnly(),
                                })
                            .OrderBy(group => group.Milestone, StringComparer.OrdinalIgnoreCase)
                            .ToList()
                            .AsReadOnly()
                },

                GroupByRepo = new GroupByRepoViewModel
                {
                    Repos =
                        workingIssues
                            .GroupBy(issue => issue.RepoName)
                            .Select(group =>
                                new GroupByRepoRepo
                                {
                                    RepoName = group.Key,
                                    Issues = group.ToList().AsReadOnly(),
                                })
                            .OrderByDescending(group => group.Issues.Count)
                            .ToList()
                            .AsReadOnly()
                },

                PullRequests = allPullRequests,
            });
        }
    }
}
